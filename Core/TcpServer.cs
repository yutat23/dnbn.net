using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Dnbn.Configuration;
using Dnbn.Filters;
using Dnbn.Models;
using Microsoft.Extensions.Logging;

namespace Dnbn.Core;

/// <summary>
/// TCPサーバー実装
/// </summary>
public class TcpServer : ITcpServer, IAsyncDisposable
{
  private readonly ServerConfig _config;
  private readonly ILogger<TcpServer>? _logger;
  private readonly List<IMessageFilter> _filters;
  private TcpListener? _listener;
  private readonly ConcurrentDictionary<string, ServerSession> _sessions = new();
  private readonly SafeObservable<(Message message, SessionInfo sessionInfo)> _messageReceivedSubject = new();
  private CancellationTokenSource _cancellationTokenSource = new();
  private bool _disposed = false;
  private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
  private readonly ConcurrentDictionary<long, Task> _clientTasks = new();
  private readonly AsyncLocal<long?> _currentClientTaskId = new();
  private Task? _acceptLoopTask;
  private long _nextClientTaskId;

  // 統計情報追跡用フィールド
  private DateTime? _startedAt;
  private long _totalConnections = 0;
  private DateTime? _lastClientConnectedAt;
  private DateTime? _lastClientDisconnectedAt;
  private long _messagesSent = 0;
  private long _messagesReceived = 0;
  private readonly object _statsLock = new();

  /// <summary>
  /// サーバー名
  /// </summary>
  public string Name => _config.Name;

  private volatile bool _isRunning;

  /// <summary>
  /// 実行状態
  /// </summary>
  public bool IsRunning => _isRunning;

  /// <summary>
  /// メッセージ受信イベント
  /// </summary>
  public event EventHandler<(Message message, SessionInfo sessionInfo)>? OnMessageReceived;

  /// <inheritdoc />
  public event TcpServerMessageHandler? OnMessageReceivedAsync;

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
    TcpMessengerConfigValidator.ValidateServer(config);
    _config = config.Clone();
    _logger = logger;
    _filters = filters?.ToList() ?? new List<IMessageFilter>();
  }

  /// <summary>
  /// サーバーを起動
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (_disposed) throw new ObjectDisposedException(GetType().FullName);
      if (IsRunning) return;

      cancellationToken.ThrowIfCancellationRequested();

      if (_cancellationTokenSource.IsCancellationRequested)
      {
        _cancellationTokenSource.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
      }

      var connectionCts = _cancellationTokenSource;
      var listener = new TcpListener(IPAddress.Parse(_config.BindAddress), _config.ListenPort);
      listener.Start();
      _listener = listener;
      _isRunning = true;
      lock (_statsLock) _startedAt = DateTime.UtcNow;

      _logger?.LogInformation("TCP Server '{Name}' started on port {Port}", Name, _config.ListenPort);
      _acceptLoopTask = AcceptClientsAsync(listener, connectionCts.Token);
    }
    finally
    {
      _lifecycleLock.Release();
    }
  }

  /// <summary>
  /// サーバーを停止
  /// </summary>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!_cancellationTokenSource.IsCancellationRequested) _cancellationTokenSource.Cancel();
      _isRunning = false;
      _listener?.Stop();
      _listener = null;

      if (_acceptLoopTask != null)
      {
        try { await _acceptLoopTask.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_cancellationTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        _acceptLoopTask = null;
      }

      foreach (var session in _sessions.Values.ToList())
      {
        await session.DisconnectAsync().ConfigureAwait(false);
      }

      // OnMessageReceivedAsync内からStopAsyncが呼ばれた場合、現在のセッションタスクを
      // 待つと自分自身の完了待ちになりデッドロックするため除外する。
      var currentClientTaskId = _currentClientTaskId.Value;
      var clientTasks = _clientTasks
          .Where(pair => pair.Key != currentClientTaskId)
          .Select(pair => pair.Value)
          .ToArray();
      if (clientTasks.Length > 0)
      {
        await Task.WhenAll(clientTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
      }
      _sessions.Clear();
      _clientTasks.Clear();
      lock (_statsLock) _startedAt = null;
      _logger?.LogInformation("TCP Server '{Name}' stopped", Name);
    }
    finally
    {
      _lifecycleLock.Release();
    }
  }

  private async Task AcceptClientsAsync(TcpListener listener, CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      try
      {
        var tcpClient = await AcceptTcpClientAsync(listener, cancellationToken).ConfigureAwait(false);
        var taskId = Interlocked.Increment(ref _nextClientTaskId);
        var task = ObserveClientAsync(taskId, tcpClient);
        _clientTasks[taskId] = task;
      }
      catch (OperationCanceledException)
      {
        // キャンセルされた場合は正常終了
        break;
      }
      catch (ObjectDisposedException)
      {
        break;
      }
      catch (Exception ex)
      {
        if (!cancellationToken.IsCancellationRequested)
        {
          _logger?.LogError(ex, "Error accepting client");
          SafeEventDispatcher.Invoke(OnError, this, (ex, (SessionInfo?)null),
              handlerEx => _logger?.LogError(handlerEx, "OnError handler threw an exception in server {Name}", Name));
        }
      }
    }
  }

  private static async Task<System.Net.Sockets.TcpClient> AcceptTcpClientAsync(TcpListener listener, CancellationToken cancellationToken)
  {
#if NETSTANDARD2_0
    // CancellationToken付きAcceptTcpClientAsyncは.NET 5以降のみ。
    // キャンセル後に完了した孤児Acceptは接続を破棄し、例外は観測して握りつぶす
    var acceptTask = listener.AcceptTcpClientAsync();
    try
    {
      return await acceptTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      _ = acceptTask.ContinueWith(
          static t =>
          {
            if (t.Status == TaskStatus.RanToCompletion)
            {
              t.Result.Dispose();
            }
            else
            {
              _ = t.Exception;
            }
          },
          CancellationToken.None,
          TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
      throw;
    }
#else
    return await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
#endif
  }

  private async Task ObserveClientAsync(long taskId, System.Net.Sockets.TcpClient tcpClient)
  {
    // 呼び出し側がdictionaryへ登録する機会を保証する。
    await Task.Yield();
    var previousTaskId = _currentClientTaskId.Value;
    _currentClientTaskId.Value = taskId;
    try
    {
      await HandleClientAsync(tcpClient).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
      _logger?.LogError(ex, "Unhandled TCP client session error in server {Name}", Name);
      tcpClient.Dispose();
    }
    finally
    {
      _currentClientTaskId.Value = previousTaskId;
      _clientTasks.TryRemove(taskId, out _);
    }
  }

  private async Task HandleClientAsync(System.Net.Sockets.TcpClient tcpClient)
  {
    TcpKeepAliveHelper.Apply(tcpClient.Client, _config.TcpKeepAlive);

    var remoteEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint!;
    var sessionId = GenerateSessionId(remoteEndPoint);
    var sessionInfo = new SessionInfo
    {
      SessionId = sessionId,
      SourceEndpoint = remoteEndPoint,
      // RemoteEndpointにローカル側が入っているのは歴史的経緯（互換性のため維持）
      RemoteEndpoint = (IPEndPoint)tcpClient.Client.LocalEndPoint!,
      LocalEndpoint = (IPEndPoint)tcpClient.Client.LocalEndPoint!,
      ConnectedAt = DateTime.UtcNow
    };

    var session = new ServerSession(
        sessionId,
        tcpClient,
        sessionInfo,
        _config,
        _logger,
        _filters);

    session.MessageReceivedAsync = async (msg, cancellationToken) =>
    {
      // メッセージ受信統計を更新
      Interlocked.Increment(ref _messagesReceived);
      SafeEventDispatcher.Invoke(OnMessageReceived, this, (msg, sessionInfo),
          ex => _logger?.LogError(ex, "OnMessageReceived handler threw an exception in server {Name}", Name));
      _messageReceivedSubject.Publish((msg, sessionInfo),
          ex => _logger?.LogError(ex, "MessageReceived observer threw an exception in server {Name}", Name));
      await RaiseMessageReceivedAsync(msg, sessionInfo, cancellationToken).ConfigureAwait(false);
    };

    session.OnDisconnected += () =>
    {
      _sessions.TryRemove(sessionId, out _);
      lock (_statsLock)
      {
        _lastClientDisconnectedAt = DateTime.UtcNow;
      }
      SafeEventDispatcher.Invoke(OnClientDisconnected, this, sessionInfo,
          ex => _logger?.LogError(ex, "OnClientDisconnected handler threw an exception in server {Name}", Name));
      // セッションが保持するリソース（CTS・セマフォ）を解放
      session.Dispose();
    };

    session.OnError += (ex) =>
    {
      SafeEventDispatcher.Invoke(OnError, this, (ex, (SessionInfo?)sessionInfo),
          handlerEx => _logger?.LogError(handlerEx, "OnError handler threw an exception in server {Name}", Name));
    };

    _sessions.TryAdd(sessionId, session);

    // 接続統計を更新
    Interlocked.Increment(ref _totalConnections);
    lock (_statsLock)
    {
      _lastClientConnectedAt = DateTime.UtcNow;
    }

    _logger?.LogInformation("TCP Server '{Name}' client connected: session {SessionId} from {RemoteEndPoint}",
        Name, sessionId, remoteEndPoint);

    SafeEventDispatcher.Invoke(OnClientConnected, this, sessionInfo,
        ex => _logger?.LogError(ex, "OnClientConnected handler threw an exception in server {Name}", Name));

    await session.RunAsync().ConfigureAwait(false);
  }

  private async Task RaiseMessageReceivedAsync(Message message, SessionInfo sessionInfo, CancellationToken cancellationToken)
  {
    var handler = OnMessageReceivedAsync;
    if (handler == null) return;
    foreach (TcpServerMessageHandler subscriber in handler.GetInvocationList())
    {
      try
      {
        await subscriber(message, sessionInfo, cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        _logger?.LogError(ex, "OnMessageReceivedAsync handler threw an exception in server {Name}", Name);
      }
    }
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
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task SendAsync(string sessionId, Message message, CancellationToken cancellationToken = default)
  {
    if (_sessions.TryGetValue(sessionId, out var session))
    {
      await session.SendAsync(message, cancellationToken);
      // メッセージ送信統計を更新
      Interlocked.Increment(ref _messagesSent);
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
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task BroadcastAsync(Message message, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    var tasks = _sessions.Values.Select(s => s.SendAsync(message, cancellationToken));
    await Task.WhenAll(tasks);
    // メッセージ送信統計を更新（セッション数分）
    var sessionCount = _sessions.Count;
    if (sessionCount > 0)
    {
      Interlocked.Add(ref _messagesSent, sessionCount);
    }
  }

  /// <summary>
  /// 特定セッションに文字列を送信（設定のEncodingを使用）
  /// </summary>
  /// <param name="sessionId">セッションID</param>
  /// <param name="text">送信する文字列</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task SendAsync(string sessionId, string text, CancellationToken cancellationToken = default)
  {
    var encoding = TcpMessageUtils.GetEncoding(_config.Encoding);
    var message = Message.FromString(text, encoding);
    await SendAsync(sessionId, message, cancellationToken);
  }

  /// <summary>
  /// 全セッションに文字列を送信（設定のEncodingを使用）
  /// </summary>
  /// <param name="text">送信する文字列</param>
  /// <param name="cancellationToken">キャンセレーショントークン</param>
  public async Task BroadcastAsync(string text, CancellationToken cancellationToken = default)
  {
    var encoding = TcpMessageUtils.GetEncoding(_config.Encoding);
    var message = Message.FromString(text, encoding);
    await BroadcastAsync(message, cancellationToken);
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
  /// リッスンポート
  /// </summary>
  public int ListenPort => _config.ListenPort;

  /// <summary>
  /// 接続状態情報の取得
  /// </summary>
  public ServerConnectionInfo ConnectionInfo
  {
    get
    {
      lock (_statsLock)
      {
        var uptime = _startedAt.HasValue
          ? DateTime.UtcNow - _startedAt.Value
          : (TimeSpan?)null;

        return new ServerConnectionInfo
        {
          IsRunning = IsRunning,
          StartedAt = _startedAt,
          Uptime = uptime,
          ListenPort = _config.ListenPort,
          ConnectionCount = _sessions.Count,
          TotalConnections = Interlocked.Read(ref _totalConnections),
          LastClientConnectedAt = _lastClientConnectedAt,
          LastClientDisconnectedAt = _lastClientDisconnectedAt,
          MessagesSent = Interlocked.Read(ref _messagesSent),
          MessagesReceived = Interlocked.Read(ref _messagesReceived)
        };
      }
    }
  }

  /// <summary>
  /// リソースを非同期に解放
  /// </summary>
  public async ValueTask DisposeAsync()
  {
    if (_disposed)
    {
      return;
    }
    await StopAsync().ConfigureAwait(false);
    _cancellationTokenSource.Dispose();
    _messageReceivedSubject.Dispose();
    _lifecycleLock.Dispose();
    _disposed = true;
  }

  /// <summary>
  /// リソースを解放（互換性維持のため残存。可能であれば DisposeAsync を使用してください）
  /// </summary>
  public void Dispose()
  {
    if (_disposed)
    {
      return;
    }
    // ConfigureAwait(false)を使用してデッドロックを回避
    StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    _cancellationTokenSource.Dispose();
    _messageReceivedSubject.Dispose();
    _lifecycleLock.Dispose();
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
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed = false;
    private int _disconnectState; // 0: 未切断, 1: 切断済み（二重切断・二重ログ防止）

    /// <summary>
    /// 電文内容ログの出力レベル。
    /// EnableMessageLogging有効時はInformation、無効時は従来どおりDebugで出力する
    /// </summary>
    private LogLevel MessageLogLevel => _config.EnableMessageLogging ? LogLevel.Information : LogLevel.Debug;

    public SessionInfo SessionInfo => _sessionInfo;

    public Func<Message, CancellationToken, Task>? MessageReceivedAsync { get; set; }
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

      var encoding = TcpMessageUtils.GetEncoding(config.Encoding);
      // 受信時の終端文字を決定：ReceiveMessageTerminatorが設定されている場合はそれを使用、未設定の場合はMessageTerminatorを使用
      string[]? receiveTerminators = config.ReceiveMessageTerminator ??
          (config.MessageTerminator != null ? new[] { config.MessageTerminator } : null);
      _parser = new MessageParser(
          encoding,
          receiveTerminators,
          config.FixedHeaderLength,
          config.FixedBodyLength,
          config.LengthFieldOffset,
          config.LengthFieldLength,
          config.MaxReceiveBufferBytes);

      _stream = _tcpClient.GetStream();
    }

    public Task RunAsync() => ReceiveLoopAsync();

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

            // メッセージログ出力（EnableMessageLogging有効時はInformationレベル）
            _logger?.Log(MessageLogLevel, "TCP Server '{Name}' received message from session {SessionId}: {MessageText}", _config.Name, _sessionId, filteredMessage.Text?.Trim());

            if (MessageReceivedAsync != null)
            {
              await MessageReceivedAsync(filteredMessage, _cancellationTokenSource.Token).ConfigureAwait(false);
            }
          }
        }
        catch (OperationCanceledException)
        {
          break;
        }
        catch (Exception ex)
        {
          _logger?.LogError(ex, "Error receiving data in session {SessionId}", _sessionId);
          SafeEventDispatcher.Invoke(OnError, ex,
              handlerEx => _logger?.LogError(handlerEx, "Session error handler threw for {SessionId}", _sessionId));
          break;
        }
      }

      // NW障害による切断として扱う
      await DisconnectAsync(isIntentional: false);
      SafeEventDispatcher.Invoke(OnDisconnected,
          ex => _logger?.LogError(ex, "Session disconnect handler threw for {SessionId}", _sessionId));
    }

    public async Task SendAsync(Message message, CancellationToken cancellationToken = default)
    {
      if (_stream == null || _tcpClient == null || !_tcpClient.Connected)
      {
        throw new InvalidOperationException("Not connected");
      }

      cancellationToken.ThrowIfCancellationRequested();

      // フィルターパイプラインを適用
      var filteredMessage = message;
      foreach (var filter in _filters)
      {
        var ctx = new MessageContext(_sessionInfo, true);
        filteredMessage = await filter.OnSendingAsync(filteredMessage, ctx);
      }

      // メッセージログ出力（EnableMessageLogging有効時はInformationレベル）
      _logger?.Log(MessageLogLevel, "TCP Server '{Name}' sending message to session {SessionId}: {MessageText}", _config.Name, _sessionId, filteredMessage.Text?.Trim());

      // MessageTerminatorを自動的に追加
      var data = TcpMessageUtils.AppendMessageTerminatorIfNeeded(filteredMessage, _config.MessageTerminator, _config.Encoding);

      try
      {
        // 外部CancellationTokenと内部CancellationTokenSourceを統合
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, cancellationToken);
        await _sendLock.WaitAsync(linkedCts.Token).ConfigureAwait(false);
        try
        {
          await _stream.WriteAsync(data, 0, data.Length, linkedCts.Token).ConfigureAwait(false);
          await _stream.FlushAsync(linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
          _sendLock.Release();
        }
      }
      catch (ObjectDisposedException)
      {
        // 切断処理と競合してCTS/セマフォが破棄された場合は「未接続」として扱う
        throw new InvalidOperationException("Not connected");
      }
    }

    public async Task DisconnectAsync(bool isIntentional = true)
    {
      // 受信ループ起因の切断とStopAsync/Disposeからの切断が重複しても一度だけ実行する
      if (Interlocked.Exchange(ref _disconnectState, 1) == 1)
      {
        return;
      }

      _cancellationTokenSource.Cancel();
      if (_stream != null)
      {
#if NETSTANDARD2_0
        _stream.Dispose();
        await Task.CompletedTask;
#else
        await _stream.DisposeAsync().ConfigureAwait(false);
#endif
        _stream = null;
      }
      _tcpClient?.Dispose();
      _sessionInfo.IsActive = false;

      if (isIntentional)
      {
        _logger?.LogInformation("TCP Server '{Name}' session {SessionId} disconnected", _config.Name, _sessionId);
      }
      else
      {
        _logger?.LogError("TCP Server '{Name}' session {SessionId} disconnected unexpectedly (network error)", _config.Name, _sessionId);
      }
    }

    public void Dispose()
    {
      if (_disposed)
      {
        return;
      }

      // ConfigureAwait(false)を使用してデッドロックを回避
      // （切断済みの場合は即座に完了する）
      DisconnectAsync().ConfigureAwait(false).GetAwaiter().GetResult();
      _tcpClient.Dispose();
      _cancellationTokenSource.Dispose();
      _sendLock.Dispose();
      _disposed = true;
    }
  }

}
