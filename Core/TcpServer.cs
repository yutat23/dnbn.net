using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Text;
using Dnbn.Configuration;
using Dnbn.Filters;
using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

/// <summary>
/// TCPサーバー実装
/// </summary>
public class TcpServer : ITcpServer
{
  private readonly ServerConfig _config;
  private readonly ILogger<TcpServer>? _logger;
  private readonly List<IMessageFilter> _filters;
  private TcpListener? _listener;
  private readonly ConcurrentDictionary<string, ServerSession> _sessions = new();
  private readonly Subject<(Message message, SessionInfo sessionInfo)> _messageReceivedSubject = new();
  private readonly CancellationTokenSource _cancellationTokenSource = new();
  private bool _disposed = false;

  /// <summary>
  /// サーバー名
  /// </summary>
  public string Name => _config.Name;

  /// <summary>
  /// 実行状態
  /// </summary>
  public bool IsRunning => _listener != null;

  /// <summary>
  /// メッセージ受信イベント
  /// </summary>
  public event EventHandler<(Message message, SessionInfo sessionInfo)>? OnMessageReceived;

  /// <summary>
  /// クライアント接続イベント
  /// </summary>
  public event EventHandler<SessionInfo>? OnClientConnected;

  /// <summary>
  /// クライアント切断イベント
  /// </summary>
  public event EventHandler<SessionInfo>? OnClientDisconnected;

  /// <summary>
  /// エラーイベント
  /// </summary>
  public event EventHandler<(Exception exception, SessionInfo? sessionInfo)>? OnError;

  /// <summary>
  /// メッセージ受信のObservable
  /// </summary>
  public IObservable<(Message message, SessionInfo sessionInfo)> MessageReceived => _messageReceivedSubject;

  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <param name="config">サーバー設定</param>
  /// <param name="logger">ロガー（オプション）</param>
  /// <param name="filters">メッセージフィルター（オプション）</param>
  public TcpServer(ServerConfig config, ILogger<TcpServer>? logger = null, IEnumerable<IMessageFilter>? filters = null)
  {
    _config = config;
    _logger = logger;
    _filters = filters?.ToList() ?? new List<IMessageFilter>();
  }

  /// <summary>
  /// サーバーを起動
  /// </summary>
  public async Task StartAsync()
  {
    if (IsRunning)
    {
      return;
    }

    _listener = new TcpListener(IPAddress.Any, _config.ListenPort);
    _listener.Start();
    _logger?.LogInformation("TCP Server '{Name}' started on port {Port}", Name, _config.ListenPort);

    _ = Task.Run(AcceptClientsAsync, _cancellationTokenSource.Token);
  }

  /// <summary>
  /// サーバーを停止
  /// </summary>
  public async Task StopAsync()
  {
    if (!IsRunning)
    {
      return;
    }

    _cancellationTokenSource.Cancel();
    _listener?.Stop();

    // 全セッションを切断
    var sessions = _sessions.Values.ToList();
    foreach (var session in sessions)
    {
      await session.DisconnectAsync();
    }
    _sessions.Clear();

    _logger?.LogInformation("TCP Server '{Name}' stopped", Name);
  }

  private async Task AcceptClientsAsync()
  {
    while (!_cancellationTokenSource.Token.IsCancellationRequested && _listener != null)
    {
      try
      {
        var tcpClient = await _listener.AcceptTcpClientAsync();
        _ = Task.Run(() => HandleClientAsync(tcpClient), _cancellationTokenSource.Token);
      }
      catch (ObjectDisposedException)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "Error accepting client");
        OnError?.Invoke(this, (ex, null));
      }
    }
  }

  private async Task HandleClientAsync(System.Net.Sockets.TcpClient tcpClient)
  {
    var remoteEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint!;
    var sessionId = GenerateSessionId(remoteEndPoint);
    var sessionInfo = new SessionInfo
    {
      SessionId = sessionId,
      SourceEndpoint = remoteEndPoint,
      RemoteEndpoint = (IPEndPoint)tcpClient.Client.LocalEndPoint!,
      ConnectedAt = DateTime.UtcNow
    };

    var session = new ServerSession(
        sessionId,
        tcpClient,
        sessionInfo,
        _config,
        _logger,
        _filters);

    session.OnMessageReceived += (msg) =>
    {
      OnMessageReceived?.Invoke(this, (msg, sessionInfo));
      _messageReceivedSubject.OnNext((msg, sessionInfo));
    };

    session.OnDisconnected += () =>
    {
      _sessions.TryRemove(sessionId, out _);
      OnClientDisconnected?.Invoke(this, sessionInfo);
    };

    session.OnError += (ex) =>
    {
      OnError?.Invoke(this, (ex, sessionInfo));
    };

    _sessions.TryAdd(sessionId, session);
    OnClientConnected?.Invoke(this, sessionInfo);

    await session.StartAsync();
  }

  private string GenerateSessionId(IPEndPoint endPoint)
  {
    return $"{endPoint.Address}:{endPoint.Port}-{Guid.NewGuid():N}";
  }

  /// <summary>
  /// 指定セッションにメッセージを送信
  /// </summary>
  /// <param name="sessionId">セッションID</param>
  /// <param name="message">送信するメッセージ</param>
  public async Task SendAsync(string sessionId, Message message)
  {
    if (_sessions.TryGetValue(sessionId, out var session))
    {
      await session.SendAsync(message);
    }
    else
    {
      throw new InvalidOperationException($"Session '{sessionId}' not found");
    }
  }

  /// <summary>
  /// 全セッションにメッセージをブロードキャスト
  /// </summary>
  /// <param name="message">送信するメッセージ</param>
  public async Task BroadcastAsync(Message message)
  {
    var tasks = _sessions.Values.Select(s => s.SendAsync(message));
    await Task.WhenAll(tasks);
  }

  /// <summary>
  /// 指定セッションの情報を取得
  /// </summary>
  /// <param name="sessionId">セッションID</param>
  /// <returns>セッション情報（見つからない場合はnull）</returns>
  public SessionInfo? GetSession(string sessionId)
  {
    return _sessions.TryGetValue(sessionId, out var session) ? session.SessionInfo : null;
  }

  /// <summary>
  /// 全セッションの情報を取得
  /// </summary>
  /// <returns>全セッション情報の列挙</returns>
  public IEnumerable<SessionInfo> GetAllSessions()
  {
    return _sessions.Values.Select(s => s.SessionInfo);
  }

  /// <summary>
  /// リソースを解放
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }

    StopAsync().GetAwaiter().GetResult();
    _cancellationTokenSource.Dispose();
    _messageReceivedSubject.Dispose();
    _disposed = true;
  }

  private class ServerSession : IDisposable
  {
    private readonly string _sessionId;
    private readonly System.Net.Sockets.TcpClient _tcpClient;
    private readonly SessionInfo _sessionInfo;
    private readonly ServerConfig _config;
    private readonly ILogger? _logger;
    private readonly List<IMessageFilter> _filters;
    private readonly MessageParser _parser;
    private NetworkStream? _stream;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _disposed = false;

    public SessionInfo SessionInfo => _sessionInfo;

    public event Action<Message>? OnMessageReceived;
    public event Action? OnDisconnected;
    public event Action<Exception>? OnError;

    public ServerSession(
        string sessionId,
        System.Net.Sockets.TcpClient tcpClient,
        SessionInfo sessionInfo,
        ServerConfig config,
        ILogger? logger,
        List<IMessageFilter> filters)
    {
      _sessionId = sessionId;
      _tcpClient = tcpClient;
      _sessionInfo = sessionInfo;
      _config = config;
      _logger = logger;
      _filters = filters;

      var encoding = GetEncoding(config.Encoding);
      _parser = new MessageParser(
          encoding,
          config.MessageTerminator,
          config.FixedHeaderLength,
          config.FixedBodyLength,
          config.LengthFieldOffset,
          config.LengthFieldLength);

      _stream = _tcpClient.GetStream();
    }

    public async Task StartAsync()
    {
      _ = Task.Run(ReceiveLoopAsync, _cancellationTokenSource.Token);
    }

    private async Task ReceiveLoopAsync()
    {
      var buffer = new byte[4096];

      while (!_cancellationTokenSource.Token.IsCancellationRequested && _stream != null)
      {
        try
        {
          var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token);
          if (bytesRead == 0)
          {
            // 接続が閉じられた
            break;
          }

          var data = new byte[bytesRead];
          Array.Copy(buffer, data, bytesRead);

          var messages = _parser.Parse(data);
          foreach (var message in messages)
          {
            _sessionInfo.LastMessageReceivedAt = DateTime.UtcNow;

            // フィルターパイプラインを適用
            var filteredMessage = message;
            foreach (var filter in _filters)
            {
              var ctx = new MessageContext(_sessionInfo, true);
              filteredMessage = await filter.OnReceivedAsync(filteredMessage, ctx);
            }

            OnMessageReceived?.Invoke(filteredMessage);
          }
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (Exception ex)
        {
          _logger?.LogError(ex, "Error receiving data in session {SessionId}", _sessionId);
          OnError?.Invoke(ex);
          break;
        }
      }

      // NW障害による切断として扱う
      await DisconnectAsync(isIntentional: false);
      OnDisconnected?.Invoke();
    }

    public async Task SendAsync(Message message)
    {
      if (_stream == null || _tcpClient == null || !_tcpClient.Connected)
      {
        throw new InvalidOperationException("Not connected");
      }

      // フィルターパイプラインを適用
      var filteredMessage = message;
      foreach (var filter in _filters)
      {
        var ctx = new MessageContext(_sessionInfo, true);
        filteredMessage = await filter.OnSendingAsync(filteredMessage, ctx);
      }

      // MessageTerminatorを自動的に追加
      var data = AppendMessageTerminatorIfNeeded(filteredMessage);
      await _stream.WriteAsync(data);
      await _stream.FlushAsync();
    }

    public async Task DisconnectAsync(bool isIntentional = true)
    {
      _cancellationTokenSource.Cancel();
      if (_stream != null)
      {
        await _stream.DisposeAsync();
        _stream = null;
      }
      _tcpClient?.Dispose();

      if (isIntentional)
      {
        _logger?.LogInformation("Session {SessionId} disconnected", _sessionId);
      }
      else
      {
        _logger?.LogError("Session {SessionId} disconnected unexpectedly (network error)", _sessionId);
      }
    }

    /// <summary>
    /// MessageTerminatorが設定されている場合、メッセージに自動的に追加する
    /// </summary>
    private byte[] AppendMessageTerminatorIfNeeded(Message message)
    {
      if (string.IsNullOrEmpty(_config.MessageTerminator))
      {
        return message.RawData;
      }

      var encoding = GetEncoding(_config.Encoding);
      var terminatorBytes = encoding.GetBytes(_config.MessageTerminator);
      
      // 既に終端文字が含まれているかチェック（末尾に一致するか）
      if (message.RawData.Length >= terminatorBytes.Length)
      {
        var suffix = new byte[terminatorBytes.Length];
        Array.Copy(message.RawData, message.RawData.Length - terminatorBytes.Length, suffix, 0, terminatorBytes.Length);
        if (suffix.SequenceEqual(terminatorBytes))
        {
          // 既に終端文字が含まれている場合は追加しない
          return message.RawData;
        }
      }

      // 終端文字を追加
      var result = new byte[message.RawData.Length + terminatorBytes.Length];
      Array.Copy(message.RawData, 0, result, 0, message.RawData.Length);
      Array.Copy(terminatorBytes, 0, result, message.RawData.Length, terminatorBytes.Length);
      return result;
    }

    private Encoding GetEncoding(string encodingName)
    {
      return encodingName.ToUpperInvariant() switch
      {
        "UTF-8" => Encoding.UTF8,
        "SHIFT-JIS" or "SHIFTJIS" => Encoding.GetEncoding("shift_jis"),
        "ASCII" => Encoding.ASCII,
        _ => Encoding.UTF8
      };
    }

    public void Dispose()
    {
      if (_disposed)
      {
        return;
      }

      DisconnectAsync().GetAwaiter().GetResult();
      _tcpClient.Dispose();
      _cancellationTokenSource.Dispose();
      _disposed = true;
    }
  }

  private class MessageContext : IMessageContext
  {
    public SessionInfo? SessionInfo { get; }
    public bool IsServerSide { get; }
    public Dictionary<string, object> Properties { get; } = new();

    public MessageContext(SessionInfo? sessionInfo, bool isServerSide)
    {
      SessionInfo = sessionInfo;
      IsServerSide = isServerSide;
    }
  }
}

