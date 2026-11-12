namespace Drover.Server.Tests.Support;

/// <summary>One captured log call: where it came from and everything it printed, exception included.</summary>
public sealed record LogEntry(string Category, LogLevel Level, string Text);

/// <summary>Keeps every log entry of a test run, at every level, so a test can look for what must never be there.</summary>
public sealed class LogCapture : ILoggerProvider
{
    #region Private Fields

    private readonly List<LogEntry> entries = [];

    #endregion

    #region Properties

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (entries)
            {
                return [.. entries];
            }
        }
    }

    #endregion

    #region Interface Implementations

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, this);

    public void Dispose()
    {
    }

    #endregion

    #region Public Methods

    public void Add(LogEntry entry)
    {
        lock (entries)
        {
            entries.Add(entry);
        }
    }

    public string AllText() => string.Join('\n', Entries.Select(entry => entry.Text));

    #endregion

    #region Nested Types

    private sealed class CapturingLogger(string category, LogCapture capture) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            capture.Add(new LogEntry(category, logLevel, $"{formatter(state, exception)} {exception}"));
    }

    #endregion
}
