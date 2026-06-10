using System.Text;
using Microsoft.Extensions.Logging;
using TcpMessenger.Sample.Scenarios;

namespace TcpMessenger.Sample;

/// <summary>
/// dnbn.net サンプル集のエントリポイント。
/// シナリオ番号を引数で指定（例: dotnet run -- 3）するか、メニューから選択する。
/// </summary>
class Program
{
  private static readonly (string Title, Func<ILoggerFactory, Task> Run)[] Scenarios =
  {
    ("クイックスタート（エコーサーバーとクライアントの最小構成）", Scenario01_QuickStart.RunAsync),
    ("チャット／ブロードキャスト（複数クライアントとサーバープッシュ）", Scenario02_ChatBroadcast.RunAsync),
    ("障害と自動再接続（サーバー停止からの自動復帰）", Scenario03_Resilience.RunAsync),
    ("KeepAliveと死活監視（応答判定と無応答の検出）", Scenario04_KeepAlive.RunAsync),
    ("レガシープロトコル（Shift-JIS・固定長・長さフィールド）", Scenario05_LegacyProtocols.RunAsync),
    ("リクエスト制御（タイムアウト・リトライ・応答の述語マッチング）", Scenario06_RequestControl.RunAsync),
    ("運用監視（メッセージフィルター・統計情報・Web UI）", Scenario07_Monitoring.RunAsync),
    ("対話プレイグラウンド（DI構成で自由にメッセージ送受信）", Scenario08_Playground.RunAsync),
  };

  static async Task Main(string[] args)
  {
    Console.OutputEncoding = Encoding.UTF8;

    using var loggerFactory = LoggerFactory.Create(builder => builder
        .AddSimpleConsole(options =>
        {
          options.SingleLine = true;
          options.TimestampFormat = "HH:mm:ss ";
        })
        .SetMinimumLevel(LogLevel.Information));

    // 引数で番号指定された場合はそのシナリオだけ実行して終了
    if (args.Length > 0)
    {
      if (TryParseScenarioNumber(args[0], out var index))
      {
        await RunScenarioAsync(index, loggerFactory);
      }
      else
      {
        Console.WriteLine($"無効なシナリオ番号です: {args[0]} (1-{Scenarios.Length} を指定してください)");
      }
      return;
    }

    // メニューループ
    while (true)
    {
      ShowMenu();
      Console.Write($"選択 (1-{Scenarios.Length}, q=終了): ");
      var input = Console.ReadLine()?.Trim();

      if (string.IsNullOrEmpty(input) || input.Equals("q", StringComparison.OrdinalIgnoreCase))
      {
        break;
      }

      if (TryParseScenarioNumber(input, out var index))
      {
        await RunScenarioAsync(index, loggerFactory);
        Console.WriteLine();
        Console.WriteLine("Enterキーでメニューに戻ります...");
        Console.ReadLine();
      }
      else
      {
        Console.WriteLine("無効な選択です。");
      }
    }
  }

  private static void ShowMenu()
  {
    Console.WriteLine();
    Console.WriteLine("=== dnbn.net サンプルシナリオ ===");
    for (int i = 0; i < Scenarios.Length; i++)
    {
      Console.WriteLine($"  {i + 1}. {Scenarios[i].Title}");
    }
  }

  private static bool TryParseScenarioNumber(string input, out int index)
  {
    if (int.TryParse(input, out var number) && number >= 1 && number <= Scenarios.Length)
    {
      index = number - 1;
      return true;
    }
    index = -1;
    return false;
  }

  private static async Task RunScenarioAsync(int index, ILoggerFactory loggerFactory)
  {
    var (title, run) = Scenarios[index];
    SampleConsole.Title($"シナリオ {index + 1}: {title}");
    try
    {
      await run(loggerFactory);
      SampleConsole.Success("シナリオが完了しました。");
    }
    catch (Exception ex)
    {
      SampleConsole.Error($"シナリオの実行中にエラーが発生しました: {ex.Message}");
      Console.WriteLine(ex);
    }
  }
}
