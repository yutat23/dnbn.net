using Microsoft.Extensions.Logging;

namespace Dnbn.Logging;

/// <summary>
/// log4netのILogをMicrosoft.Extensions.Logging.ILoggerにアダプトするクラス
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
/// <typeparam name="T">ロガーのカテゴリ型</typeparam>
[Obsolete("このクラスは非推奨です。アプリ側でMicrosoft.Extensions.Logging.Log4Net.AspNetCoreを使用してください。")]
public class Log4netLoggerAdapter<T> : ILogger<T>
{
  /// <summary>
  /// コンストラクタ
  /// </summary>
  /// <exception cref="NotSupportedException">log4netへの直接依存は削除されました。アプリ側でMicrosoft.Extensions.Logging.Log4Net.AspNetCoreを使用してください。</exception>
  public Log4netLoggerAdapter()
  {
    throw new NotSupportedException(
      "log4netへの直接依存は削除されました。アプリ側でMicrosoft.Extensions.Logging.Log4Net.AspNetCoreパッケージをインストールし、" +
      "services.AddLogging(builder => builder.AddLog4Net())を使用してください。");
  }

  /// <summary>
  /// ログスコープを開始（log4netでは未サポートのため、空の実装）
  /// </summary>
  public IDisposable? BeginScope<TState>(TState state) where TState : notnull
  {
    return null;
  }

  /// <summary>
  /// 指定されたログレベルが有効かどうかを判定
  /// </summary>
  public bool IsEnabled(LogLevel logLevel)
  {
    return false;
  }

  /// <summary>
  /// ログを出力
  /// </summary>
  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
  {
    // 何もしない（既にコンストラクタで例外がスローされる）
  }
}
