namespace Drover.Presentation.Diagnostics;

/// <summary>
/// Puts the app's warnings and failures in the diagnostic log, through the logging the app already uses. Each entry
/// is made by <see cref="DiagnosticEntry"/>, which keeps only what is known to be safe.
/// </summary>
public sealed class DiagnosticLogProvider(DiagnosticLog log, TimeProvider time) : ILoggerProvider
{
    #region Interface Implementations

    public ILogger CreateLogger(string categoryName) => new Writer(log, time, categoryName);

    public void Dispose()
    {
    }

    #endregion

    #region Writer

    private sealed class Writer(DiagnosticLog log, TimeProvider time, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Warning and < LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                log.Write(DiagnosticEntry.Line(
                    time.GetUtcNow(), logLevel, category, eventId, state as IReadOnlyList<KeyValuePair<string, object?>>, exception));
            }
        }
    }

    #endregion
}
