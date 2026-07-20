namespace Dnbn.Sample;

/// <summary>
/// シナリオの進行を見やすく表示するためのコンソール出力ヘルパー
/// </summary>
internal static class SampleConsole
{
  /// <summary>シナリオ全体のタイトル</summary>
  public static void Title(string text)
  {
    Console.WriteLine();
    WriteColored($"━━━ {text} ━━━", ConsoleColor.Cyan);
  }

  /// <summary>ストーリー上のステップ（何をしようとしているか）</summary>
  public static void Step(string text)
  {
    Console.WriteLine();
    WriteColored($"▶ {text}", ConsoleColor.Yellow);
  }

  /// <summary>補足説明</summary>
  public static void Note(string text)
  {
    WriteColored($"  ※ {text}", ConsoleColor.DarkGray);
  }

  /// <summary>結果の表示</summary>
  public static void Result(string text)
  {
    Console.WriteLine($"  → {text}");
  }

  /// <summary>成功</summary>
  public static void Success(string text)
  {
    WriteColored($"✓ {text}", ConsoleColor.Green);
  }

  /// <summary>失敗・エラー</summary>
  public static void Error(string text)
  {
    WriteColored($"✗ {text}", ConsoleColor.Red);
  }

  private static void WriteColored(string text, ConsoleColor color)
  {
    var original = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ForegroundColor = original;
  }
}
