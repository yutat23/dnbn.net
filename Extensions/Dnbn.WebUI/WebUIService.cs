using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dnbn.WebUI;

/// <summary>
/// Web UIサービス（TCP Messengerの状態をWebブラウザで表示）
/// </summary>
public class WebUIService : IDisposable
{
  private readonly List<ITcpServer> _servers;
  private readonly List<ITcpClient> _clients;
  private readonly WebUIConfig _config;
  private readonly ILogger? _logger;
  private readonly CancellationTokenSource _cancellationTokenSource = new();
  private readonly ConcurrentBag<StreamWriter> _sseConnections = new();
  private readonly List<Action> _eventUnsubscribers = new();
  private Timer? _updateTimer;
  private WebApplication? _app;
  private readonly JsonSerializerOptions _jsonOptions = new()
  {
    WriteIndented = false, // 1行形式で送信
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  /// <summary>
  /// コンストラクタ
  /// </summary>
  public WebUIService(
      IEnumerable<ITcpServer> servers,
      IEnumerable<ITcpClient> clients,
      WebUIConfig config,
      ILogger? logger = null)
  {
    _servers = servers.ToList();
    _clients = clients.ToList();
    _config = config;
    _logger = logger;
  }

  /// <summary>
  /// Web UIサーバーを起動
  /// </summary>
  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (!_config.Enabled)
    {
      return;
    }

    try
    {
      // 設定の検証
      ValidateConfig();

      // WebApplicationを作成
      var builder = WebApplication.CreateBuilder();
      
      // CORSサービスを追加
      builder.Services.AddCors(options =>
      {
        options.AddDefaultPolicy(policy =>
        {
          policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
      });
      
      _app = builder.Build();

      // CORSミドルウェアを使用
      _app.UseCors();

      // ルートパスでindex.htmlを返す
      _app.MapGet("/", () =>
      {
        var html = GetEmbeddedResource("index.html");
        return Results.Content(html, "text/html; charset=utf-8");
      });

      // CSSファイルを返す
      _app.MapGet("/css/output.css", () =>
      {
        var css = GetEmbeddedResource("css/output.css");
        return Results.Content(css, "text/css; charset=utf-8");
      });

      // JavaScriptファイルを返す
      _app.MapGet("/js/app.js", () =>
      {
        var js = GetEmbeddedResource("js/app.js");
        return Results.Content(js, "application/javascript; charset=utf-8");
      });

      // SSEストリームエンドポイント
      _app.MapGet("/api/status/stream", async (HttpContext context) =>
      {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["Connection"] = "keep-alive";

        StreamWriter? writer = null;
        try
        {
          writer = new StreamWriter(context.Response.Body, Encoding.UTF8) { AutoFlush = false };
          _sseConnections.Add(writer);

          if (_config.EnableLogging)
          {
            _logger?.LogDebug("SSE接続が確立されました");
          }

          // 接続確認メッセージを送信
          await SendSSEMessage(writer, new { type = "connected", timestamp = DateTime.UtcNow });

          // 初期状態を送信
          await SendStatusUpdate(writer);

          // 接続が維持されている間、待機（HttpContext.RequestAbortedを使用）
          await Task.Delay(Timeout.Infinite, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
          // キャンセルされた場合は正常終了
        }
        catch (Exception ex)
        {
          if (_config.EnableLogging)
          {
            _logger?.LogError(ex, "SSEストリームでエラーが発生しました: {Message}", ex.Message);
          }
          
          // エラーメッセージを送信しようとする
          if (writer != null)
          {
            try
            {
              await SendSSEMessage(writer, new { type = "error", message = ex.Message, timestamp = DateTime.UtcNow });
            }
            catch
            {
              // エラー送信に失敗した場合は無視
            }
          }
        }
        finally
        {
          if (writer != null)
          {
            _sseConnections.TryTake(out _);
            try
            {
              writer.Dispose();
            }
            catch (Exception ex)
            {
              // Dispose時のエラーは無視するが、ログに記録
              _logger?.LogDebug(ex, "SSE接続のDispose中にエラーが発生しました（無視されます）");
            }
          }
          
          if (_config.EnableLogging)
          {
            _logger?.LogDebug("SSE接続が切断されました");
          }
        }
      });

      // 全接続状態情報を取得
      _app.MapGet("/api/status", () =>
      {
        var status = GetStatus();
        return Results.Json(status, _jsonOptions);
      });

      // クライアント接続状態情報を取得
      _app.MapGet("/api/status/client", () =>
      {
        var clients = GetClientStatuses();
        return Results.Json(new { clients }, _jsonOptions);
      });

      // サーバー接続状態情報を取得
      _app.MapGet("/api/status/server", () =>
      {
        var servers = GetServerStatuses();
        return Results.Json(new { servers }, _jsonOptions);
      });

      // ヘルスチェックエンドポイント
      _app.MapGet("/api/health", () =>
      {
        return Results.Json(new
        {
          status = "Healthy",
          timestamp = DateTime.UtcNow
        }, _jsonOptions);
      });

      // TCP Messengerのイベントを監視
      RegisterEventHandlers();

      // 定期的な更新タイマーを開始
      _updateTimer = new Timer(async _ => await NotifyAllConnections(), null,
          TimeSpan.Zero,
          TimeSpan.FromSeconds(_config.UpdateIntervalSeconds));

      // Webサーバーを起動
      var bindAddress = _config.BindAddress == "*" ? "0.0.0.0" : _config.BindAddress;
      var url = $"http://{bindAddress}:{_config.Port}";

      if (_config.EnableLogging)
      {
        _logger?.LogInformation("Web UIサーバーを起動しています: {Url}", url);
      }

      _app.Urls.Add(url);

      // 外部CancellationTokenがキャンセルされたときに内部CancellationTokenSourceもキャンセルする
      cancellationToken.Register(() =>
      {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
          _cancellationTokenSource.Cancel();
        }
      });

      _ = Task.Run(async () =>
      {
        try
        {
          await _app.StartAsync(_cancellationTokenSource.Token);
          // CancellationTokenがキャンセルされるまで待機
          try
          {
            await Task.Delay(Timeout.Infinite, _cancellationTokenSource.Token);
          }
          catch (OperationCanceledException)
          {
            // 正常終了
          }
        }
        catch (OperationCanceledException)
        {
          // キャンセルされた場合は正常終了
        }
        catch (Exception ex)
        {
          _logger?.LogError(ex, "Web UIサーバーの起動に失敗しました");
        }
      }, _cancellationTokenSource.Token);

      if (_config.EnableLogging)
      {
        _logger?.LogInformation("Web UIサーバーが起動しました: {Url}", url);
      }
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Web UIの起動中にエラーが発生しました");
      throw;
    }
  }

  /// <summary>
  /// Web UIサーバーを停止
  /// </summary>
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (_config.EnableLogging)
    {
      _logger?.LogInformation("Web UIサーバーを停止しています");
    }

    _cancellationTokenSource.Cancel();
    _updateTimer?.Dispose();

    // イベントハンドラを解除
    foreach (var unsubscribe in _eventUnsubscribers)
    {
      try
      {
        unsubscribe();
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "イベントハンドラの解除中にエラーが発生しました");
      }
    }
    _eventUnsubscribers.Clear();

    // SSE接続を閉じる（Webアプリケーション停止前に明示的に閉じる）
    foreach (var writer in _sseConnections)
    {
      try
      {
        writer.Dispose();
      }
      catch { }
    }
    _sseConnections.Clear();

    // Webアプリケーションを停止（タイムアウト付き）
    if (_app != null)
    {
      try
      {
        // タイムアウト付きで停止を試みる
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopCts.CancelAfter(TimeSpan.FromSeconds(5)); // 5秒でタイムアウト

        try
        {
          await _app.StopAsync(stopCts.Token);
        }
        catch (OperationCanceledException)
        {
          if (_config.EnableLogging)
          {
            _logger?.LogWarning("Webアプリケーションの停止がタイムアウトしました。強制終了します。");
          }
        }

        // DisposeAsyncもタイムアウト付きで実行
        try
        {
          using var disposeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
          disposeCts.CancelAfter(TimeSpan.FromSeconds(2)); // 2秒でタイムアウト
          var disposeTask = _app.DisposeAsync().AsTask();
          await disposeTask.WaitAsync(disposeCts.Token);
        }
        catch (OperationCanceledException)
        {
          if (_config.EnableLogging)
          {
            _logger?.LogWarning("WebアプリケーションのDisposeがタイムアウトしました。");
          }
        }
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "Webアプリケーションの停止中にエラーが発生しました");
      }
    }

    if (_config.EnableLogging)
    {
      _logger?.LogInformation("Web UIサーバーを停止しました");
    }
  }

  /// <summary>
  /// 設定を検証
  /// </summary>
  private void ValidateConfig()
  {
    if (_config.Port < 1 || _config.Port > 65535)
    {
      throw new ArgumentException($"ポート番号が無効です: {_config.Port} (範囲: 1-65535)");
    }

    if (_config.UpdateIntervalSeconds < 1)
    {
      throw new ArgumentException($"更新間隔が無効です: {_config.UpdateIntervalSeconds}秒 (1秒以上を推奨)");
    }
  }

  /// <summary>
  /// TCP Messengerのイベントハンドラを登録
  /// </summary>
  private void RegisterEventHandlers()
  {
    // クライアントのイベントを監視
    foreach (var client in _clients)
    {
      client.OnConnected += async (sender, args) => await NotifyAllConnections();
      client.OnDisconnected += async (sender, args) => await NotifyAllConnections();
      client.OnMessageReceived += async (sender, message) => await NotifyAllConnections();
      client.OnKeepAliveResponseReceived += async (sender, message) => await NotifyAllConnections();
      client.OnError += async (sender, exception) => await NotifyAllConnections();

      // イベントハンドラの解除用アクションを保存
      _eventUnsubscribers.Add(() =>
      {
        client.OnConnected -= async (sender, args) => await NotifyAllConnections();
        client.OnDisconnected -= async (sender, args) => await NotifyAllConnections();
        client.OnMessageReceived -= async (sender, message) => await NotifyAllConnections();
        client.OnKeepAliveResponseReceived -= async (sender, message) => await NotifyAllConnections();
        client.OnError -= async (sender, exception) => await NotifyAllConnections();
      });
    }

    // サーバーのイベントを監視
    foreach (var server in _servers)
    {
      server.OnClientConnected += async (sender, sessionInfo) => await NotifyAllConnections();
      server.OnClientDisconnected += async (sender, sessionInfo) => await NotifyAllConnections();
      server.OnMessageReceived += async (sender, args) => await NotifyAllConnections();
      server.OnError += async (sender, args) => await NotifyAllConnections();

      // イベントハンドラの解除用アクションを保存
      _eventUnsubscribers.Add(() =>
      {
        server.OnClientConnected -= async (sender, sessionInfo) => await NotifyAllConnections();
        server.OnClientDisconnected -= async (sender, sessionInfo) => await NotifyAllConnections();
        server.OnMessageReceived -= async (sender, args) => await NotifyAllConnections();
        server.OnError -= async (sender, args) => await NotifyAllConnections();
      });
    }
  }

  /// <summary>
  /// すべてのSSE接続に状態更新を送信
  /// </summary>
  private async Task NotifyAllConnections()
  {
    if (_sseConnections.IsEmpty)
    {
      return;
    }

    var status = GetStatus();
    var json = JsonSerializer.Serialize(status, _jsonOptions);

    var deadConnections = new List<StreamWriter>();

    foreach (var writer in _sseConnections)
    {
      try
      {
        await SendSSEMessage(writer, status);
      }
      catch
      {
        // 接続が切断された場合は後で削除
        deadConnections.Add(writer);
      }
    }

    // 切断された接続を削除
    foreach (var dead in deadConnections)
    {
      _sseConnections.TryTake(out _);
      try
      {
        dead.Dispose();
      }
      catch (Exception ex)
      {
        // Dispose時のエラーは無視するが、ログに記録
        _logger?.LogDebug(ex, "SSE接続のDispose中にエラーが発生しました（無視されます）");
      }
    }
  }

  /// <summary>
  /// SSEメッセージを送信
  /// </summary>
  private async Task SendSSEMessage(StreamWriter writer, object data)
  {
    try
    {
      var json = JsonSerializer.Serialize(data, _jsonOptions);
      await writer.WriteAsync($"data: {json}\n\n");
      await writer.FlushAsync();
    }
    catch (Exception ex)
    {
      if (_config.EnableLogging)
      {
        _logger?.LogError(ex, "SSEメッセージの送信中にエラーが発生しました: {Message}", ex.Message);
      }
      throw;
    }
  }

  /// <summary>
  /// 状態更新を送信（個別の接続用）
  /// </summary>
  private async Task SendStatusUpdate(StreamWriter writer)
  {
    try
    {
      var status = GetStatus();
      await SendSSEMessage(writer, status);
    }
    catch (Exception ex)
    {
      if (_config.EnableLogging)
      {
        _logger?.LogError(ex, "状態更新の送信中にエラーが発生しました: {Message}", ex.Message);
      }
      // エラー情報を送信
      await SendSSEMessage(writer, new { type = "error", message = ex.Message, timestamp = DateTime.UtcNow });
    }
  }

  /// <summary>
  /// 全状態情報を取得
  /// </summary>
  private object GetStatus()
  {
    try
    {
      return new
      {
        clients = GetClientStatuses(),
        servers = GetServerStatuses(),
        timestamp = DateTime.UtcNow
      };
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "状態情報の取得中にエラーが発生しました: {Message}", ex.Message);
      return new
      {
        clients = Array.Empty<object>(),
        servers = Array.Empty<object>(),
        timestamp = DateTime.UtcNow,
        error = ex.Message
      };
    }
  }

  /// <summary>
  /// クライアント状態情報のリストを取得
  /// </summary>
  private object[] GetClientStatuses()
  {
    return _clients.Select<ITcpClient, object>(client =>
    {
      try
      {
        var info = client.ConnectionInfo;
        if (info == null)
        {
          return new
          {
            name = client.Name ?? "Unknown",
            isConnected = false,
            error = "ConnectionInfo is null"
          };
        }

        return new
        {
          name = client.Name ?? "Unknown",
          isConnected = info.IsConnected,
          connectedAt = info.ConnectedAt,
          connectionDuration = info.ConnectionDuration?.ToString(@"dd\.hh\:mm\:ss"),
          remoteHost = info.RemoteHost ?? string.Empty,
          remotePort = info.RemotePort,
          isReconnecting = info.IsReconnecting,
          messagesSent = info.MessagesSent,
          messagesReceived = info.MessagesReceived,
          pendingRequests = info.PendingRequests,
          lastMessageReceivedAt = info.LastMessageReceivedAt,
          keepAlive = new
          {
            lastSentAt = info.LastKeepAliveSentAt,
            lastResponseReceivedAt = info.LastKeepAliveResponseReceivedAt,
            timeoutCount = info.KeepAliveTimeoutCount
          },
          error = new
          {
            count = info.ErrorCount,
            lastError = info.LastError ?? string.Empty,
            lastErrorAt = info.LastErrorAt
          },
          connectionRetry = new
          {
            attempts = info.ConnectionRetryAttempts,
            lastAttemptAt = info.LastRetryAttemptAt
          }
        };
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "クライアント状態情報の取得中にエラーが発生しました: {ClientName}", client.Name);
        return new
        {
          name = client.Name ?? "Unknown",
          isConnected = false,
          error = $"Error: {ex.Message}"
        };
      }
    }).ToArray();
  }

  /// <summary>
  /// サーバー状態情報のリストを取得
  /// </summary>
  private object[] GetServerStatuses()
  {
    return _servers.Select<ITcpServer, object>(server =>
    {
      try
      {
        var info = server.ConnectionInfo;
        if (info == null)
        {
          return new
          {
            name = server.Name ?? "Unknown",
            isRunning = false,
            error = "ConnectionInfo is null"
          };
        }

        object[] sessions = Array.Empty<object>();
        try
        {
          sessions = server.GetAllSessions().Select(s => new
          {
            sessionId = s.SessionId ?? string.Empty,
            sourceEndpoint = s.SourceEndpoint?.ToString() ?? "Unknown",
            connectedAt = s.ConnectedAt,
            lastMessageReceivedAt = s.LastMessageReceivedAt,
            isActive = s.IsActive
          }).Cast<object>().ToArray();
        }
        catch (Exception ex)
        {
          _logger?.LogError(ex, "サーバーセッション情報の取得中にエラーが発生しました: {ServerName}", server.Name);
          sessions = Array.Empty<object>();
        }

        return new
        {
          name = server.Name ?? "Unknown",
          isRunning = info.IsRunning,
          listenPort = info.ListenPort,
          startedAt = info.StartedAt,
          uptime = info.Uptime?.ToString(@"dd\.hh\:mm\:ss"),
          connectionCount = info.ConnectionCount,
          totalConnections = info.TotalConnections,
          lastClientConnectedAt = info.LastClientConnectedAt,
          lastClientDisconnectedAt = info.LastClientDisconnectedAt,
          messagesSent = info.MessagesSent,
          messagesReceived = info.MessagesReceived,
          sessions
        };
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "サーバー状態情報の取得中にエラーが発生しました: {ServerName}", server.Name);
        return new
        {
          name = server.Name ?? "Unknown",
          isRunning = false,
          error = $"Error: {ex.Message}"
        };
      }
    }).ToArray();
  }

  /// <summary>
  /// 埋め込みリソースを取得
  /// </summary>
  private string GetEmbeddedResource(string path)
  {
    var assembly = Assembly.GetExecutingAssembly();
    var assemblyName = assembly.GetName().Name ?? "Dnbn.WebUI";
    var resourceName = $"{assemblyName}.wwwroot.{path.Replace('/', '.')}";

    using var stream = assembly.GetManifestResourceStream(resourceName);
    if (stream == null)
    {
      // デバッグ用: 利用可能なリソース名をログに出力
      var availableResources = assembly.GetManifestResourceNames()
          .Where(n => n.Contains("wwwroot"))
          .ToArray();
      var availableResourcesList = string.Join(", ", availableResources);
      throw new FileNotFoundException($"埋め込みリソースが見つかりません: {resourceName}. 利用可能なリソース: {availableResourcesList}");
    }

    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
  }

  /// <summary>
  /// リソースを解放
  /// </summary>
  public void Dispose()
  {
    _cancellationTokenSource.Cancel();
    _updateTimer?.Dispose();
    _cancellationTokenSource.Dispose();

    foreach (var writer in _sseConnections)
    {
      try
      {
        writer.Dispose();
      }
      catch (Exception ex)
      {
        // Dispose時のエラーは無視するが、ログに記録
        _logger?.LogDebug(ex, "SSE接続のDispose中にエラーが発生しました（無視されます）");
      }
    }
    _sseConnections.Clear();

    foreach (var unsubscribe in _eventUnsubscribers)
    {
      try
      {
        unsubscribe();
      }
      catch (Exception ex)
      {
        // イベント購読解除時のエラーは無視するが、ログに記録
        _logger?.LogDebug(ex, "イベント購読解除中にエラーが発生しました（無視されます）");
      }
    }
    _eventUnsubscribers.Clear();

    // ConfigureAwait(false)を使用してデッドロックを回避
    if (_app != null)
    {
      try
      {
        _app.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
      }
      catch (Exception ex)
      {
        // Dispose時のエラーはログに記録するが、例外は再スローしない
        // （Disposeメソッドは例外をスローすべきではない）
        _logger?.LogDebug(ex, "WebアプリケーションのDispose中にエラーが発生しました（無視されます）");
      }
    }
  }
}
