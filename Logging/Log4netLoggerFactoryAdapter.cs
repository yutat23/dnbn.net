using Microsoft.Extensions.Logging;

namespace Dnbn.Logging;

/// <summary>
/// log4netのLogManagerをMicrosoft.Extensions.Logging.ILoggerFactoryにアダプトするクラス
/// </summary>
/// <remarks>
/// <para>
/// このクラスは互換性のために残されていますが、<strong>非推奨</strong>です。
/// </para>
/// <para>
/// <strong>推奨される方法</strong>：アプリ側で<see href="https://www.nuget.org/packages/Microsoft.Extensions.Logging.Log4Net.AspNetCore">Microsoft.Extensions.Logging.Log4Net.AspNetCore</see>パッケージを使用してください。
/// </para>
/// <para>
/// アプリ側での使用例：
/// <code>
/// services.AddLogging(builder => builder.AddLog4Net());
/// services.AddTcpMessenger(configuration);
/// </code>
/// </para>
/// </remarks>
[Obsolete("このクラスは非推奨です。アプリ側でMicrosoft.Extensions.Logging.Log4Net.AspNetCoreを使用してください。")]
public class Log4netLoggerFactoryAdapter : ILoggerFactory
{
  /// <summary>
  /// ログプロバイダーを追加（log4netでは未使用のため、空の実装）
  /// </summary>
  public void AddProvider(ILoggerProvider provider)
  {
    // log4netは既に設定済みのため、プロバイダー追加は不要
  }

  /// <summary>
  /// 指定されたカテゴリ名のロガーを作成
  /// </summary>
  public ILogger CreateLogger(string categoryName)
  {
    throw new NotSupportedException(
      "log4netへの直接依存は削除されました。アプリ側でMicrosoft.Extensions.Logging.Log4Net.AspNetCoreパッケージをインストールし、" +
      "services.AddLogging(builder => builder.AddLog4Net())を使用してください。");
  }

  /// <summary>
  /// リソースを破棄
  /// </summary>
  public void Dispose()
  {
    // log4netのリソース管理は不要
  }

}
