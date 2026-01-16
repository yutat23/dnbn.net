using Microsoft.Extensions.Logging;
using log4net;

namespace Dnbn.Logging;

/// <summary>
/// log4netのLogManagerをMicrosoft.Extensions.Logging.ILoggerFactoryにアダプトするクラス
/// </summary>
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
    var log4netLogger = LogManager.GetLogger(categoryName);
    return new Log4netLogger(categoryName, log4netLogger);
  }

  /// <summary>
  /// リソースを破棄
  /// </summary>
  public void Dispose()
  {
    // log4netのリソース管理は不要
  }

  /// <summary>
  /// 非ジェネリック版のlog4netロガーアダプター
  /// </summary>
  private class Log4netLogger : ILogger
  {
    private readonly string _categoryName;
    private readonly ILog _log4netLogger;

    public Log4netLogger(string categoryName, ILog log4netLogger)
    {
      _categoryName = categoryName;
      _log4netLogger = log4netLogger;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
      return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
      return logLevel switch
      {
        LogLevel.Trace or LogLevel.Debug => _log4netLogger.IsDebugEnabled,
        LogLevel.Information => _log4netLogger.IsInfoEnabled,
        LogLevel.Warning => _log4netLogger.IsWarnEnabled,
        LogLevel.Error => _log4netLogger.IsErrorEnabled,
        LogLevel.Critical => _log4netLogger.IsFatalEnabled,
        LogLevel.None => false,
        _ => false
      };
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
      if (!IsEnabled(logLevel))
      {
        return;
      }

      var message = formatter(state, exception);

      switch (logLevel)
      {
        case LogLevel.Trace:
        case LogLevel.Debug:
          if (exception != null)
          {
            _log4netLogger.Debug(message, exception);
          }
          else
          {
            _log4netLogger.Debug(message);
          }

          break;

        case LogLevel.Information:
          if (exception != null)
          {
            _log4netLogger.Info(message, exception);
          }
          else
          {
            _log4netLogger.Info(message);
          }

          break;

        case LogLevel.Warning:
          if (exception != null)
          {
            _log4netLogger.Warn(message, exception);
          }
          else
          {
            _log4netLogger.Warn(message);
          }

          break;

        case LogLevel.Error:
          if (exception != null)
          {
            _log4netLogger.Error(message, exception);
          }
          else
          {
            _log4netLogger.Error(message);
          }

          break;

        case LogLevel.Critical:
          if (exception != null)
          {
            _log4netLogger.Fatal(message, exception);
          }
          else
          {
            _log4netLogger.Fatal(message);
          }

          break;
      }
    }
  }
}
