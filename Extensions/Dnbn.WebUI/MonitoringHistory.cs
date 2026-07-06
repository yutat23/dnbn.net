namespace Dnbn.WebUI;

/// <summary>
/// 固定容量のスレッドセーフな履歴バッファ（容量超過時は古いものから破棄）。
/// メモリ使用量は 容量 × 1件あたりのサイズ で上限が決まる
/// </summary>
internal sealed class BoundedHistory<T>
{
  private readonly Queue<T> _items = new();
  private readonly int _capacity;
  private readonly object _lock = new();

  public BoundedHistory(int capacity)
  {
    _capacity = Math.Max(1, capacity);
  }

  public void Add(T item)
  {
    lock (_lock)
    {
      _items.Enqueue(item);
      while (_items.Count > _capacity)
      {
        _items.Dequeue();
      }
    }
  }

  public IReadOnlyList<T> Snapshot()
  {
    lock (_lock)
    {
      return _items.ToList();
    }
  }
}

/// <summary>
/// イベントタイムラインの1エントリ（接続/切断/エラーなど）。
/// SourceType は Client / Server のいずれか
/// </summary>
internal sealed record TimelineEntry(
    DateTime Timestamp,
    string Source,
    string SourceType,
    string Type,
    string? Detail);

/// <summary>
/// メッセージログの1エントリ。SourceType は Client / Server のいずれか。
/// ペイロードは切り詰め済みの文字列のみを保持する
/// （Message本体への参照を持たないため、保持件数×最大ペイロードでメモリ上限が決まる）
/// </summary>
internal sealed record MessageLogEntry(
    DateTime Timestamp,
    string Source,
    string SourceType,
    string Direction,
    string Kind,
    string? Text,
    int SizeBytes,
    string? Hex,
    double? ElapsedMs);
