using Dnbn.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dnbn.Extensions;

/// <summary>
/// 設定に定義された全クライアントへの名前付きアクセスを提供するコレクション。
/// クライアントは keyed service（キー = クライアント名）として登録されており、
/// <c>GetRequiredKeyedService&lt;ITcpClient&gt;(name)</c> でも直接取得できる。
/// </summary>
public interface IDnbnClientCollection
{
  /// <summary>設定に定義された全クライアント名</summary>
  IReadOnlyList<string> Names { get; }

  /// <summary>名前を指定してクライアントを取得（未定義の名前は null）</summary>
  ITcpClient? GetClient(string name);

  /// <summary>全クライアントを取得</summary>
  IEnumerable<ITcpClient> GetAllClients();
}

/// <summary>
/// ライブラリが所有し、Hostライフサイクルと連動する名前付きクライアントのregistry。
/// </summary>
public interface IDnbnClientRegistry : IDnbnClientCollection
{
}

/// <summary>
/// IDnbnClientCollection の実装（keyed service 経由でクライアントを解決する）
/// </summary>
internal sealed class DnbnClientCollection : IDnbnClientRegistry
{
  private readonly IServiceProvider _serviceProvider;

  public DnbnClientCollection(IServiceProvider serviceProvider, IReadOnlyList<string> names)
  {
    _serviceProvider = serviceProvider;
    Names = names;
  }

  /// <inheritdoc />
  public IReadOnlyList<string> Names { get; }

  /// <inheritdoc />
  public ITcpClient? GetClient(string name)
      => Names.Contains(name) ? _serviceProvider.GetRequiredKeyedService<ITcpClient>(name) : null;

  /// <inheritdoc />
  public IEnumerable<ITcpClient> GetAllClients()
      => Names.Select(name => _serviceProvider.GetRequiredKeyedService<ITcpClient>(name));
}

/// <summary>
/// アプリ起動時に設定済みの全クライアントを接続し、シャットダウン時に切断する Hosted Service。
/// 接続はバックグラウンドで開始され（ホストの起動をブロックしない）、
/// 接続失敗時は各クライアントの ConnectionRetryPolicy に従ってライブラリ側でリトライされる。
/// </summary>
internal sealed class DnbnClientsHostedService : IHostedService, IDisposable
{
  private readonly IDnbnClientCollection _clients;
  private readonly ILogger<DnbnClientsHostedService>? _logger;
  private readonly CancellationTokenSource _stoppingCts = new();
  private readonly List<Task> _connectionTasks = new();
  private readonly object _sync = new();

  public DnbnClientsHostedService(IDnbnClientCollection clients, ILogger<DnbnClientsHostedService>? logger = null)
  {
    _clients = clients;
    _logger = logger;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    lock (_sync)
    {
      if (_connectionTasks.Count != 0)
      {
        return Task.CompletedTask;
      }

      foreach (var client in _clients.GetAllClients())
      {
        var name = client.Name;
        // ConnectionRetryPolicy によっては接続完了まで長時間かかる（無限リトライ含む）ため、
        // ホストの起動をブロックしない。タスクは停止時の競合防止のため保持する。
        _connectionTasks.Add(Task.Run(async () =>
        {
          try
          {
            await client.ConnectAsync(_stoppingCts.Token).ConfigureAwait(false);
          }
          catch (OperationCanceledException) when (_stoppingCts.IsCancellationRequested)
          {
            // ホスト停止による正常な中断
          }
          catch (Exception ex)
          {
            _logger?.LogWarning(ex, "TCP クライアント接続に失敗しました: Name={Name}", name);
          }
        }, CancellationToken.None));
      }
    }

    return Task.CompletedTask;
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    _stoppingCts.Cancel();

    Task[] connectionTasks;
    lock (_sync)
    {
      connectionTasks = _connectionTasks.ToArray();
    }
    try
    {
      await Task.WhenAll(connectionTasks).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // ホストの停止猶予を超えた場合も、各クライアントの切断を試みる。
    }

    foreach (var client in _clients.GetAllClients())
    {
      try
      {
        await client.DisconnectAsync(isIntentional: true, cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("TCP クライアント切断: Name={Name}", client.Name);
      }
      catch (Exception ex)
      {
        _logger?.LogWarning(ex, "TCP クライアント切断に失敗しました: Name={Name}", client.Name);
      }
    }
  }

  public void Dispose()
  {
    _stoppingCts.Cancel();
    _stoppingCts.Dispose();
  }
}
