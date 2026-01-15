using Microsoft.Extensions.Logging;
using log4net;

namespace Dnbn.Logging;

/// <summary>
/// log4netのILogをMicrosoft.Extensions.Logging.ILoggerにアダプトするクラス
/// </summary>
/// <typeparam name="T">ロガーのカテゴリ型</typeparam>
public class Log4netLoggerAdapter<T> : ILogger<T>
{
    private readonly ILog _log4netLogger;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public Log4netLoggerAdapter()
    {
        _log4netLogger = LogManager.GetLogger(typeof(T));
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

    /// <summary>
    /// ログを出力
    /// </summary>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);

        switch (logLevel)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
                if (exception != null)
                    _log4netLogger.Debug(message, exception);
                else
                    _log4netLogger.Debug(message);
                break;

            case LogLevel.Information:
                if (exception != null)
                    _log4netLogger.Info(message, exception);
                else
                    _log4netLogger.Info(message);
                break;

            case LogLevel.Warning:
                if (exception != null)
                    _log4netLogger.Warn(message, exception);
                else
                    _log4netLogger.Warn(message);
                break;

            case LogLevel.Error:
                if (exception != null)
                    _log4netLogger.Error(message, exception);
                else
                    _log4netLogger.Error(message);
                break;

            case LogLevel.Critical:
                if (exception != null)
                    _log4netLogger.Fatal(message, exception);
                else
                    _log4netLogger.Fatal(message);
                break;
        }
    }
}
