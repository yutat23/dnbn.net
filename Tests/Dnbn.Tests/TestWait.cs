using System.Text;

namespace Dnbn.Tests;

/// <summary>
/// テスト用の条件待機ヘルパー（タイミング依存のテストを決定的にするため）
/// </summary>
internal static class TestWait
{
  /// <summary>条件が成立するまでポーリングで待機する。タイムアウト時は TimeoutException</summary>
  public static async Task UntilAsync(Func<bool> condition, int timeoutMs = 3000, int pollMs = 10)
  {
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (!condition())
    {
      if (DateTime.UtcNow > deadline)
      {
        throw new TimeoutException($"条件が {timeoutMs}ms 以内に成立しませんでした");
      }
      await Task.Delay(pollMs);
    }
  }

  /// <summary>指定文字列を含むデータが MockTransport から送信されるまで待機する</summary>
  public static Task UntilSentAsync(MockTransport transport, string contains, int timeoutMs = 3000)
      => UntilAsync(
          () => transport.SentData.Any(d => Encoding.UTF8.GetString(d).Contains(contains)),
          timeoutMs);

  /// <summary>送信データ件数が指定数以上になるまで待機する</summary>
  public static Task UntilSentCountAsync(MockTransport transport, int count, int timeoutMs = 3000)
      => UntilAsync(() => transport.SentData.Count >= count, timeoutMs);
}
