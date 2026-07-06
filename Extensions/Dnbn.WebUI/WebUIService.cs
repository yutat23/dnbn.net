using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dnbn.Configuration;
using Dnbn.Core;
using Dnbn.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
  // ConcurrentBag.TryTakeは「任意の1件」を取り除くため、切断された接続の代わりに
  // 生きている接続を管理外にしてしまう。特定のwriterを確実に除去できるDictionaryを使う
  private readonly ConcurrentDictionary<StreamWriter, byte> _sseConnections = new();
  private readonly Dictionary<ITcpClient, ClientEventHandlers> _clientHandlers = new();
  private readonly Dictionary<ITcpServer, ServerEventHandlers> _serverHandlers = new();
  private readonly BoundedHistory<TimelineEntry> _timeline;
  private readonly BoundedHistory<MessageLogEntry>? _messageHistory;
  // タイマーとイベントからの同時通知を1回に集約し、同じSSE writerへの並行書き込みを防ぐ。
  private readonly SemaphoreSlim _notifyLock = new(1, 1);
  private readonly object _sync = new();
  private readonly object _stopSync = new();
  private Timer? _updateTimer;
  private WebApplication? _app;
  private Task? _stopTask;
  private CancellationTokenRegistration _externalCancellationTokenRegistration;
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
    _timeline = new BoundedHistory<TimelineEntry>(config.EventTimelineCapacity);
    _messageHistory = config.EnableMessageHistory
        ? new BoundedHistory<MessageLogEntry>(config.MessageHistoryCapacity)
        : null;
  }

  /// <summary>
  /// Web UIの監視対象にクライアントを追加
  /// </summary>
  /// <returns>追加に成功した場合はtrue、既に追加済みの場合はfalse</returns>
  public bool AddClient(ITcpClient client)
  {
    if (client == null)
    {
      throw new ArgumentNullException(nameof(client));
    }

    lock (_sync)
    {
      if (_clients.Contains(client))
      {
        return false;
      }

      _clients.Add(client);
      SubscribeClient(client);
    }

    _ = NotifyAllConnections();
    return true;
  }

  /// <summary>
  /// Web UIの監視対象にサーバーを追加
  /// </summary>
  /// <returns>追加に成功した場合はtrue、既に追加済みの場合はfalse</returns>
  public bool AddServer(ITcpServer server)
  {
    if (server == null)
    {
      throw new ArgumentNullException(nameof(server));
    }

    lock (_sync)
    {
      if (_servers.Contains(server))
      {
        return false;
      }

      _servers.Add(server);
      SubscribeServer(server);
    }

    _ = NotifyAllConnections();
    return true;
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

      // WebUIは外側のアプリケーションに埋め込まれる補助ホスト。
      // 既定のConsoleLifetimeにCtrl-Cを横取りさせず、外側のホストまたは
      // StartAsyncへ渡されたCancellationTokenでライフサイクルを管理する。
      builder.Services.AddSingleton<IHostLifetime, EmbeddedWebUIHostLifetime>();
      
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
          writer = new StreamWriter(context.Response.Body, new UTF8Encoding(false)) { AutoFlush = false };

          if (_config.EnableLogging)
          {
            _logger?.LogDebug("SSE接続が確立されました");
          }

          // 接続確認メッセージを送信
          await SendSSEMessage(writer, new { type = "connected", timestamp = DateTime.UtcNow });

          // 初期状態を送信
          await SendStatusUpdate(writer);

          // 初期メッセージへの並行書き込みを避けるため、初期送信後にブロードキャスト対象へ加える。
          _sseConnections.TryAdd(writer, 0);

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
            _sseConnections.TryRemove(writer, out _);
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

      // イベントタイムライン（接続/切断/状態遷移/エラーの履歴）
      _app.MapGet("/api/timeline", (string? source, string? sourceType) =>
      {
        var events = _timeline.Snapshot()
            .Where(entry => MatchesSource(entry.Source, entry.SourceType, source, sourceType))
            .ToArray();
        return Results.Json(new { events }, _jsonOptions);
      });

      // メッセージログ（EnableMessageHistory有効時のみ）
      _app.MapGet("/api/messages", (string? source, string? sourceType) =>
      {
        if (_messageHistory == null)
        {
          return Results.Json(new { enabled = false, messages = Array.Empty<MessageLogEntry>() }, _jsonOptions);
        }
        var messages = _messageHistory.Snapshot()
            .Where(entry => MatchesSource(entry.Source, entry.SourceType, source, sourceType))
            .ToArray();
        return Results.Json(new { enabled = true, messages }, _jsonOptions);
      });

      // アナライズ（メッセージログからの応答時間統計）
      _app.MapGet("/api/analytics", (string? source, string? sourceType) =>
      {
        return Results.Json(new
        {
          enabled = _messageHistory != null,
          clients = ComputeAnalytics(source, sourceType)
        }, _jsonOptions);
      });

      // Web UIからのメッセージ送信（AllowSendFromUI有効時のみ）
      _app.MapPost("/api/send", async (HttpContext context) =>
      {
        if (!_config.AllowSendFromUI)
        {
          return Results.Json(new { error = "Web UIからの送信は無効です（WebUIConfig.AllowSendFromUI）" },
              _jsonOptions, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!string.IsNullOrEmpty(_config.SendAuthToken))
        {
          var token = context.Request.Headers["X-Dnbn-Send-Token"].ToString();
          if (!TokenEquals(token, _config.SendAuthToken))
          {
            return Results.Json(new { error = "認証トークンが一致しません" },
                _jsonOptions, statusCode: StatusCodes.Status401Unauthorized);
          }
        }

        SendRequestDto? request;
        try
        {
          request = await context.Request.ReadFromJsonAsync<SendRequestDto>(context.RequestAborted);
        }
        catch (Exception ex) when (ex is JsonException or BadHttpRequestException or NotSupportedException)
        {
          request = null;
        }

        if (request == null || string.IsNullOrEmpty(request.Client) || request.Text == null)
        {
          return Results.Json(new { error = "リクエスト形式が不正です（client と text が必須）" },
              _jsonOptions, statusCode: StatusCodes.Status400BadRequest);
        }

        ITcpClient? client;
        lock (_sync)
        {
          client = _clients.FirstOrDefault(c => c.Name == request.Client);
        }

        if (client == null)
        {
          return Results.Json(new { error = $"クライアントが見つかりません: {request.Client}" },
              _jsonOptions, statusCode: StatusCodes.Status404NotFound);
        }

        try
        {
          if (request.OneWay)
          {
            // 注意: OneWayへの応答は稼働中アプリの応答マッチングに誤マッチしうるため、
            // 応答が無い電文にのみ使用すること（UI側にも注意書きあり）
            await client.SendOneWayAsync(request.Text, context.RequestAborted);
            return Results.Json(new { success = true, response = (string?)null }, _jsonOptions);
          }

          var response = await client.SendAsync(request.Text, cancellationToken: context.RequestAborted);
          return Results.Json(new { success = true, response = response.Text }, _jsonOptions);
        }
        catch (Exception ex)
        {
          return Results.Json(new { success = false, error = $"{ex.GetType().Name}: {ex.Message}" }, _jsonOptions);
        }
      });

      // TCP Messengerのイベントを監視
      RegisterEventHandlers();

      // 定期的な更新タイマーを開始
      _updateTimer = new Timer(_ => _ = NotifyAllConnections(), null,
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

      // Kestrelの起動完了（または起動失敗）を呼び出し元へ正しく返す。
      using (var startupCts = CancellationTokenSource.CreateLinkedTokenSource(
          _cancellationTokenSource.Token, cancellationToken))
      {
        await _app.StartAsync(startupCts.Token).ConfigureAwait(false);
      }

      // 外部トークンがキャンセルされたらWebアプリケーション自体も停止する。
      _externalCancellationTokenRegistration = cancellationToken.Register(
          () => _ = StopAsync(CancellationToken.None));

      if (_config.EnableLogging)
      {
        _logger?.LogInformation("Web UIサーバーが起動しました: {Url}", url);
      }
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Web UIの起動中にエラーが発生しました");
      try
      {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
      }
      catch (Exception cleanupEx)
      {
        _logger?.LogDebug(cleanupEx, "Web UIの起動失敗後のクリーンアップに失敗しました");
      }
      throw;
    }
  }

  /// <summary>
  /// Web UIサーバーを停止
  /// </summary>
  public Task StopAsync(CancellationToken cancellationToken = default)
  {
    lock (_stopSync)
    {
      return _stopTask ??= StopCoreAsync(cancellationToken);
    }
  }

  private async Task StopCoreAsync(CancellationToken cancellationToken)
  {
    if (_config.EnableLogging)
    {
      _logger?.LogInformation("Web UIサーバーを停止しています");
    }

    _cancellationTokenSource.Cancel();
    _updateTimer?.Dispose();

    // イベントハンドラを解除
    lock (_sync)
    {
      foreach (var pair in _clientHandlers)
      {
        UnsubscribeClient(pair.Key, pair.Value);
      }
      _clientHandlers.Clear();

      foreach (var pair in _serverHandlers)
      {
        UnsubscribeServer(pair.Key, pair.Value);
      }
      _serverHandlers.Clear();
    }

    // SSE接続を閉じる（Webアプリケーション停止前に明示的に閉じる）
    foreach (var writer in _sseConnections.Keys)
    {
      try
      {
        writer.Dispose();
      }
      catch { }
    }
    _sseConnections.Clear();

    // Webアプリケーションを停止（タイムアウト付き）
    var app = Interlocked.Exchange(ref _app, null);
    if (app != null)
    {
      try
      {
        // タイムアウト付きで停止を試みる
        using var stopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stopCts.CancelAfter(TimeSpan.FromSeconds(5)); // 5秒でタイムアウト

        try
        {
          await app.StopAsync(stopCts.Token).ConfigureAwait(false);
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
          var disposeTask = app.DisposeAsync().AsTask();
          await disposeTask.WaitAsync(disposeCts.Token).ConfigureAwait(false);
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


    if (_config.EventTimelineCapacity < 1)
    {
      throw new ArgumentException($"イベントタイムライン保持件数が無効です: {_config.EventTimelineCapacity} (1以上)");
    }

    if (_config.EnableMessageHistory && _config.MessageHistoryCapacity < 1)
    {
      throw new ArgumentException($"メッセージ履歴保持件数が無効です: {_config.MessageHistoryCapacity} (1以上)");
    }

    if (_config.EnableMessageHistory && _config.MessageHistoryMaxPayloadBytes < 1)
    {
      throw new ArgumentException($"メッセージ履歴ペイロード上限が無効です: {_config.MessageHistoryMaxPayloadBytes} (1以上)");
    }
  }

  /// <summary>
  /// TCP Messengerのイベントハンドラを登録
  /// </summary>
  private void RegisterEventHandlers()
  {
    lock (_sync)
    {
      // クライアントのイベントを監視
      foreach (var client in _clients)
      {
        SubscribeClient(client);
      }

      // サーバーのイベントを監視
      foreach (var server in _servers)
      {
        SubscribeServer(server);
      }
    }
  }

  private void SubscribeClient(ITcpClient client)
  {
    if (_clientHandlers.ContainsKey(client))
    {
      return;
    }

    var name = client.Name;

    // SSEの即時通知は接続状態の変化・エラーのみ。
    // メッセージ受信のたびに全状態を全接続へ送ると高トラフィック時にSSEが洪水になるため、
    // 統計値の更新は定期更新タイマー（UpdateIntervalSeconds）に任せる
    var handlers = new ClientEventHandlers
    {
      Connected = (sender, args) =>
      {
        _timeline.Add(new TimelineEntry(DateTime.UtcNow, name, "Client", "Connected", null));
        _ = NotifyAllConnections();
      },
      Disconnected = (sender, args) =>
      {
        _timeline.Add(new TimelineEntry(DateTime.UtcNow, name, "Client", "Disconnected", null));
        _ = NotifyAllConnections();
      },
      Error = (sender, exception) =>
      {
        _timeline.Add(new TimelineEntry(DateTime.UtcNow, name, "Client", "Error", exception.Message));
        _ = NotifyAllConnections();
      },
      StateChanged = (sender, e) =>
      {
        _timeline.Add(new TimelineEntry(DateTime.UtcNow, name, "Client", "StateChanged", $"{e.previous} -> {e.current}"));
      },
      MessageTrace = _messageHistory != null
          ? (sender, e) => _messageHistory.Add(CreateMessageLogEntry(name, e))
          : null,
    };

    client.OnConnected += handlers.Connected;
    client.OnDisconnected += handlers.Disconnected;
    client.OnError += handlers.Error;
    client.OnConnectionStateChanged += handlers.StateChanged;
    if (handlers.MessageTrace != null)
    {
      client.OnMessageTrace += handlers.MessageTrace;
    }

    _clientHandlers.Add(client, handlers);
    AddInitialClientTimelineEntry(client);
  }

  private void SubscribeServer(ITcpServer server)
  {
    if (_serverHandlers.ContainsKey(server))
    {
      return;
    }

    var name = server.Name;

    var handlers = new ServerEventHandlers
    {
      ClientConnected = (sender, sessionInfo) =>
      {
        _timeline.Add(new TimelineEntry(DateTime.UtcNow, name, "Server", "ClientConnected", sessionInfo.SourceEndpoint?.ToString()));
        _ = NotifyAllConnections();
      },
      ClientDisconnected = (sender, sessionInfo) =>
      {
        _timeline.Add(new TimelineEntry(DateTime.UtcNow, name, "Server", "ClientDisconnected", sessionInfo.SourceEndpoint?.ToString()));
        _ = NotifyAllConnections();
      },
      // メッセージ受信は即時通知しない（定期更新タイマーに任せる）。
      // サーバー側の送信はタップポイントがないため、メッセージログは受信のみ記録する
      MessageReceived = _messageHistory != null
          ? (sender, args) => _messageHistory.Add(CreateServerReceivedLogEntry(name, args.message))
          : null,
      Error = (sender, args) =>
      {
        _timeline.Add(new TimelineEntry(DateTime.UtcNow, name, "Server", "Error", args.exception.Message));
        _ = NotifyAllConnections();
      }
    };

    server.OnClientConnected += handlers.ClientConnected;
    server.OnClientDisconnected += handlers.ClientDisconnected;
    if (handlers.MessageReceived != null)
    {
      server.OnMessageReceived += handlers.MessageReceived;
    }
    server.OnError += handlers.Error;

    _serverHandlers.Add(server, handlers);
    AddInitialServerTimelineEntries(server);
  }

  private static void UnsubscribeClient(ITcpClient client, ClientEventHandlers handlers)
  {
    if (handlers.Connected != null)
    {
      client.OnConnected -= handlers.Connected;
    }
    if (handlers.Disconnected != null)
    {
      client.OnDisconnected -= handlers.Disconnected;
    }
    if (handlers.Error != null)
    {
      client.OnError -= handlers.Error;
    }
    if (handlers.StateChanged != null)
    {
      client.OnConnectionStateChanged -= handlers.StateChanged;
    }
    if (handlers.MessageTrace != null)
    {
      client.OnMessageTrace -= handlers.MessageTrace;
    }
  }

  /// <summary>
  /// WebUI開始前に接続済みだったクライアントも、ConnectionInfoの接続時刻を使って
  /// タイムラインへ復元する。未接続の場合は監視開始時点の状態を記録する。
  /// </summary>
  private void AddInitialClientTimelineEntry(ITcpClient client)
  {
    try
    {
      var info = client.ConnectionInfo;
      if (info.IsConnected)
      {
        _timeline.Add(new TimelineEntry(
            info.ConnectedAt ?? DateTime.UtcNow,
            client.Name,
            "Client",
            "Connected",
            "Observed when WebUI monitoring started"));
      }
      else
      {
        _timeline.Add(new TimelineEntry(
            DateTime.UtcNow,
            client.Name,
            "Client",
            "CurrentState",
            client.State.ToString()));
      }
    }
    catch (Exception ex)
    {
      _logger?.LogDebug(ex, "クライアントの初期状態をタイムラインへ追加できませんでした: {Name}", client.Name);
    }
  }

  /// <summary>
  /// WebUI開始前から起動・接続済みのサーバー状態とセッションをタイムラインへ復元する。
  /// </summary>
  private void AddInitialServerTimelineEntries(ITcpServer server)
  {
    try
    {
      var info = server.ConnectionInfo;
      _timeline.Add(info.IsRunning
          ? new TimelineEntry(
              info.StartedAt ?? DateTime.UtcNow,
              server.Name,
              "Server",
              "Started",
              "Observed when WebUI monitoring started")
          : new TimelineEntry(
              DateTime.UtcNow,
              server.Name,
              "Server",
              "CurrentState",
              "Stopped"));

      foreach (var session in server.GetAllSessions())
      {
        _timeline.Add(new TimelineEntry(
            session.ConnectedAt,
            server.Name,
            "Server",
            "ClientConnected",
            $"{session.SourceEndpoint} (observed when WebUI monitoring started)"));
      }
    }
    catch (Exception ex)
    {
      _logger?.LogDebug(ex, "サーバーの初期状態をタイムラインへ追加できませんでした: {Name}", server.Name);
    }
  }

  private static void UnsubscribeServer(ITcpServer server, ServerEventHandlers handlers)
  {
    if (handlers.ClientConnected != null)
    {
      server.OnClientConnected -= handlers.ClientConnected;
    }
    if (handlers.ClientDisconnected != null)
    {
      server.OnClientDisconnected -= handlers.ClientDisconnected;
    }
    if (handlers.MessageReceived != null)
    {
      server.OnMessageReceived -= handlers.MessageReceived;
    }
    if (handlers.Error != null)
    {
      server.OnError -= handlers.Error;
    }
  }

  /// <summary>
  /// トレースイベントからメッセージログエントリを作成（ペイロードは設定の上限まで切り詰め）
  /// </summary>
  private MessageLogEntry CreateMessageLogEntry(string source, MessageTraceEvent e)
  {
    var (text, hex, size) = TruncatePayload(e.Message);
    return new MessageLogEntry(
        e.Timestamp, source, "Client", e.Direction.ToString(), e.Kind.ToString(),
        text, size, hex, e.ElapsedMilliseconds);
  }

  /// <summary>
  /// サーバー受信メッセージからログエントリを作成
  /// </summary>
  private MessageLogEntry CreateServerReceivedLogEntry(string source, Message message)
  {
    var (text, hex, size) = TruncatePayload(message);
    return new MessageLogEntry(
        DateTime.UtcNow, source, "Server", "Received", "Received",
        text, size, hex, null);
  }

  /// <summary>
  /// ペイロードを設定の上限バイト数まで切り詰めて文字列化する。
  /// Message本体への参照は保持しない（メモリ上限を保証するため）
  /// </summary>
  private (string? text, string? hex, int sizeBytes) TruncatePayload(Message message)
  {
    var raw = message.RawData ?? Array.Empty<byte>();
    var size = raw.Length;
    var maxBytes = Math.Max(1, _config.MessageHistoryMaxPayloadBytes);

    var truncated = size > maxBytes ? raw.AsSpan(0, maxBytes).ToArray() : raw;
    var hex = Convert.ToHexString(truncated);

    var text = message.Text;
    if (text != null && text.Length > maxBytes)
    {
      text = text[..maxBytes] + "…";
    }
    else if (text != null && size > maxBytes)
    {
      text += "…";
    }

    return (text, hex, size);
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

    // 状態変化が集中した場合は、進行中の通知の次の定期更新に集約する。
    if (!await _notifyLock.WaitAsync(0).ConfigureAwait(false))
    {
      return;
    }

    try
    {
      var status = GetStatus();

      var deadConnections = new List<StreamWriter>();

      foreach (var writer in _sseConnections.Keys)
      {
        try
        {
          await SendSSEMessage(writer, status).ConfigureAwait(false);
        }
        catch
        {
          // 接続が切断された場合は後で削除
          deadConnections.Add(writer);
        }
      }

      // 切断された接続を削除（生きている接続を巻き添えにしないよう、該当writerを指定して除去）
      foreach (var dead in deadConnections)
      {
        _sseConnections.TryRemove(dead, out _);
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
    finally
    {
      _notifyLock.Release();
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
        features = new
        {
          messageHistoryEnabled = _messageHistory != null,
          sendEnabled = _config.AllowSendFromUI,
          sendTokenRequired = !string.IsNullOrEmpty(_config.SendAuthToken),
        },
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
    List<ITcpClient> clients;
    lock (_sync)
    {
      clients = _clients.ToList();
    }

    return clients.Select<ITcpClient, object>(client =>
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
    List<ITcpServer> servers;
    lock (_sync)
    {
      servers = _servers.ToList();
    }

    return servers.Select<ITcpServer, object>(server =>
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
    try
    {
      StopAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
    }
    catch (Exception ex)
    {
      _logger?.LogDebug(ex, "Webアプリケーションの停止中にエラーが発生しました（無視されます）");
    }

    _externalCancellationTokenRegistration.Dispose();
    _cancellationTokenSource.Dispose();
  }

  /// <summary>
  /// メッセージログから応答時間統計を計算する（Response種別のみ対象）
  /// </summary>
  private object[] ComputeAnalytics(string? source = null, string? sourceType = null)
  {
    if (_messageHistory == null)
    {
      return Array.Empty<object>();
    }

    return _messageHistory.Snapshot()
        .Where(m => MatchesSource(m.Source, m.SourceType, source, sourceType))
        .Where(m => m.Kind == nameof(MessageTraceKind.Response) && m.ElapsedMs.HasValue)
        .GroupBy(m => m.Source)
        .Select(g =>
        {
          var sorted = g.Select(m => m.ElapsedMs!.Value).OrderBy(v => v).ToArray();
          var p95Index = Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1);
          return (object)new
          {
            name = g.Key,
            responseCount = sorted.Length,
            minMs = Math.Round(sorted[0], 1),
            avgMs = Math.Round(sorted.Average(), 1),
            maxMs = Math.Round(sorted[^1], 1),
            p95Ms = Math.Round(sorted[Math.Max(0, p95Index)], 1),
          };
        })
        .ToArray();
  }

  private static bool MatchesSource(
      string entrySource,
      string entrySourceType,
      string? requestedSource,
      string? requestedSourceType)
  {
    return (string.IsNullOrEmpty(requestedSource) ||
            string.Equals(entrySource, requestedSource, StringComparison.Ordinal)) &&
        (string.IsNullOrEmpty(requestedSourceType) ||
         string.Equals(entrySourceType, requestedSourceType, StringComparison.OrdinalIgnoreCase));
  }

  private static bool TokenEquals(string supplied, string expected)
  {
    var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    return suppliedBytes.Length == expectedBytes.Length &&
        CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
  }

  /// <summary>Web UIからの送信リクエスト</summary>
  private sealed class SendRequestDto
  {
    public string? Client { get; set; }
    public string? Text { get; set; }
    public bool OneWay { get; set; }
  }

  private sealed class ClientEventHandlers
  {
    public EventHandler? Connected { get; init; }
    public EventHandler? Disconnected { get; init; }
    public EventHandler<Exception>? Error { get; init; }
    public EventHandler<(ConnectionState previous, ConnectionState current)>? StateChanged { get; init; }
    public EventHandler<MessageTraceEvent>? MessageTrace { get; init; }
  }

  private sealed class ServerEventHandlers
  {
    public EventHandler<SessionInfo>? ClientConnected { get; init; }
    public EventHandler<SessionInfo>? ClientDisconnected { get; init; }
    public EventHandler<(Message message, SessionInfo sessionInfo)>? MessageReceived { get; init; }
    public EventHandler<(Exception exception, SessionInfo? sessionInfo)>? Error { get; init; }
  }

  private sealed class EmbeddedWebUIHostLifetime : IHostLifetime
  {
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
  }
}
