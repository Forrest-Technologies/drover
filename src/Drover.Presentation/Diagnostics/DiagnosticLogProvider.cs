namespace Drover.Presentation.Diagnostics;

/// <summary>
/// Puts the app's warnings and failures in the diagnostic log, through the logging the app already uses. Each entry
/// is made by <see cref="DiagnosticEntry"/>, which keeps only what is known to be safe. A platform can catch a failure
/// nothing in the app handled and only log it: <paramref name="unhandledFrom"/> names the loggers that report those,
/// and an error with an exception from one of them is kept as a failure nothing handled.
/// </summary>
public sealed class DiagnosticLogProvider(DiagnosticLog log, TimeProvider time, IReadOnlySet<string> unhandledFrom)
    : ILoggerProvider
{
    #region Interface Implementations

    public ILogger CreateLogger(string categoryName) =>
        new Writer(log, time, categoryName, unhandledFrom.Contains(categoryName));

    public void Dispose()
    {
    }

    #endregion

    #region Writer

    private sealed class Writer(DiagnosticLog log, TimeProvider time, string category, bool reportsUnhandled) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel is >= LogLevel.Warning and < LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (reportsUnhandled && logLevel >= LogLevel.Error && exception is not null)
            {
                log.RecordFailure(exception);
            }

            if (IsEnabled(logLevel))
            {
                var values = state as IReadOnlyList<KeyValuePair<string, object?>>;
                log.Write(DiagnosticEntry.Line(time.GetUtcNow(), logLevel, category, eventId, values, exception));
            }
        }
    }

    #endregion
}
