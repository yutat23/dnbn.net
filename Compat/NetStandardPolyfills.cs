// netstandard2.0 (.NET Framework 4.6.1+) 向けのポリフィル。
// net8.0 ビルドではフレームワーク標準のAPIがそのまま使われる。
#if NETSTANDARD2_0

namespace System.Runtime.CompilerServices
{
  /// <summary>init アクセサ（C# 9）をnetstandard2.0でコンパイルするためのマーカー型</summary>
  internal static class IsExternalInit { }
}

namespace Dnbn.Core
{
  /// <summary>
  /// .NET 5で追加された非ジェネリック TaskCompletionSource の代替。
  /// 使用側ファイルの using エイリアス（TaskCompletionSource = TaskCompletionSourceCompat）経由で参照され、
  /// 呼び出しコードは無変更で両ターゲットに対応できる。
  /// （InternalsVisibleToで参照するテストアセンブリと本家の型が衝突しないよう、型名は別にしている）
  /// </summary>
  internal sealed class TaskCompletionSourceCompat
  {
    private readonly TaskCompletionSource<bool> _inner;

    public TaskCompletionSourceCompat() => _inner = new TaskCompletionSource<bool>();

    public TaskCompletionSourceCompat(TaskCreationOptions creationOptions)
        => _inner = new TaskCompletionSource<bool>(creationOptions);

    public Task Task => _inner.Task;

    public bool TrySetResult() => _inner.TrySetResult(true);

    public bool TrySetCanceled() => _inner.TrySetCanceled();

    public bool TrySetCanceled(CancellationToken cancellationToken)
        => _inner.TrySetCanceled(cancellationToken);

    public bool TrySetException(Exception exception) => _inner.TrySetException(exception);
  }

  /// <summary>
  /// .NET 6で追加された Task.WaitAsync(CancellationToken) の代替。
  /// 標準実装と同様、キャンセル時はトークンを保持した TaskCanceledException を送出する。
  /// </summary>
  internal static class TaskWaitAsyncCompat
  {
    public static async Task WaitAsync(this Task task, CancellationToken cancellationToken)
    {
      if (task.IsCompleted || !cancellationToken.CanBeCanceled)
      {
        await task.ConfigureAwait(false);
        return;
      }

      cancellationToken.ThrowIfCancellationRequested();

      var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      using (cancellationToken.Register(
          static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
          cancelTcs))
      {
        var completed = await System.Threading.Tasks.Task.WhenAny(task, cancelTcs.Task).ConfigureAwait(false);
        if (completed != task)
        {
          throw new TaskCanceledException(System.Threading.Tasks.Task.FromCanceled(cancellationToken));
        }

        await task.ConfigureAwait(false);
      }
    }

    public static async Task<TResult> WaitAsync<TResult>(this Task<TResult> task, CancellationToken cancellationToken)
    {
      if (task.IsCompleted || !cancellationToken.CanBeCanceled)
      {
        return await task.ConfigureAwait(false);
      }

      cancellationToken.ThrowIfCancellationRequested();

      var cancelTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      using (cancellationToken.Register(
          static state => ((TaskCompletionSource<bool>)state!).TrySetCanceled(),
          cancelTcs))
      {
        var completed = await System.Threading.Tasks.Task.WhenAny(task, cancelTcs.Task).ConfigureAwait(false);
        if (completed != task)
        {
          throw new TaskCanceledException(System.Threading.Tasks.Task.FromCanceled(cancellationToken));
        }

        return await task.ConfigureAwait(false);
      }
    }
  }

  /// <summary>
  /// netstandard2.0向けSystem.Threading.Channelsに含まれない ChannelReader&lt;T&gt;.ReadAllAsync の代替
  /// </summary>
  internal static class ChannelReaderCompat
  {
    public static async IAsyncEnumerable<T> ReadAllAsync<T>(
        this System.Threading.Channels.ChannelReader<T> reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      while (await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
      {
        while (reader.TryRead(out var item))
        {
          yield return item;
        }
      }
    }
  }

  /// <summary>
  /// .NET 6で追加された PeriodicTimer の代替。
  /// 標準実装と同様、Dispose 後の待機は false を返し、トークンのキャンセルは
  /// OperationCanceledException を送出する。
  /// （標準実装と異なり Task.Delay ベースのため、処理時間分だけ周期がずれる）
  /// </summary>
  internal sealed class PeriodicTimer : IDisposable
  {
    private readonly TimeSpan _period;
    private readonly CancellationTokenSource _disposeCts = new CancellationTokenSource();

    public PeriodicTimer(TimeSpan period)
    {
      if (period <= TimeSpan.Zero)
      {
        throw new ArgumentOutOfRangeException(nameof(period));
      }

      _period = period;
    }

    public async Task<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
    {
      if (_disposeCts.IsCancellationRequested)
      {
        return false;
      }

      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, cancellationToken);
      try
      {
        await System.Threading.Tasks.Task.Delay(_period, linkedCts.Token).ConfigureAwait(false);
        return true;
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
        // Dispose による停止
        return false;
      }
      catch (OperationCanceledException)
      {
        throw new OperationCanceledException(cancellationToken);
      }
    }

    public void Dispose() => _disposeCts.Cancel();
  }
}

#endif
