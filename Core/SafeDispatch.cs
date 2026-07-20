namespace Dnbn.Core;

/// <summary>購読者ごとに例外を隔離してイベントを配送する。</summary>
internal static class SafeEventDispatcher
{
  public static void Invoke(EventHandler? handler, object sender, EventArgs args, Action<Exception> onError)
  {
    if (handler == null) return;
    foreach (EventHandler subscriber in handler.GetInvocationList())
    {
      try { subscriber.Invoke(sender, args); }
      catch (Exception ex) { onError(ex); }
    }
  }

  public static void Invoke<T>(EventHandler<T>? handler, object sender, T args, Action<Exception> onError)
  {
    if (handler == null) return;
    foreach (EventHandler<T> subscriber in handler.GetInvocationList())
    {
      try { subscriber.Invoke(sender, args); }
      catch (Exception ex) { onError(ex); }
    }
  }

  public static void Invoke(Action? handler, Action<Exception> onError)
  {
    if (handler == null) return;
    foreach (Action subscriber in handler.GetInvocationList())
    {
      try { subscriber.Invoke(); }
      catch (Exception ex) { onError(ex); }
    }
  }

  public static void Invoke<T>(Action<T>? handler, T args, Action<Exception> onError)
  {
    if (handler == null) return;
    foreach (Action<T> subscriber in handler.GetInvocationList())
    {
      try { subscriber.Invoke(args); }
      catch (Exception ex) { onError(ex); }
    }
  }
}

/// <summary>observer例外を通信ループから隔離する最小IObservable実装。</summary>
internal sealed class SafeObservable<T> : IObservable<T>, IDisposable
{
  private readonly object _sync = new();
  private readonly List<IObserver<T>> _observers = new();
  private bool _disposed;

  public IDisposable Subscribe(IObserver<T> observer)
  {
    if (observer is null) throw new ArgumentNullException(nameof(observer));
    lock (_sync)
    {
      if (_disposed)
      {
        observer.OnCompleted();
        return EmptySubscription.Instance;
      }
      _observers.Add(observer);
    }
    return new Subscription(this, observer);
  }

  public void Publish(T value, Action<Exception> onError)
  {
    IObserver<T>[] observers;
    lock (_sync) observers = _disposed ? [] : _observers.ToArray();
    foreach (var observer in observers)
    {
      try { observer.OnNext(value); }
      catch (Exception ex) { onError(ex); }
    }
  }

  public void Dispose()
  {
    IObserver<T>[] observers;
    lock (_sync)
    {
      if (_disposed) return;
      _disposed = true;
      observers = _observers.ToArray();
      _observers.Clear();
    }
    foreach (var observer in observers)
    {
      try { observer.OnCompleted(); }
      catch { }
    }
  }

  private void Unsubscribe(IObserver<T> observer)
  {
    lock (_sync) _observers.Remove(observer);
  }

  private sealed class Subscription(SafeObservable<T> owner, IObserver<T> observer) : IDisposable
  {
    private SafeObservable<T>? _owner = owner;
    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Unsubscribe(observer);
  }

  private sealed class EmptySubscription : IDisposable
  {
    public static readonly EmptySubscription Instance = new();
    public void Dispose() { }
  }
}
