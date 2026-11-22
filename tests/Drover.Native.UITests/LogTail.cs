namespace Drover.Native.UITests;

/// <summary>
/// The last lines the test server logged, kept so a server that never became ready can be reported with what it said.
/// </summary>
internal sealed class LogTail : ILoggerProvider
{
    #region Private Fields

    private const int Capacity = 120;

    private readonly Queue<string> lines = [];

    #endregion

    #region Public Methods

    /// <summary>The kept lines, oldest first, one per line.</summary>
    public string Text()
    {
        lock (lines)
        {
            return string.Join(Environment.NewLine, lines);
        }
    }

    #endregion

    #region Interface Implementations

    public ILogger CreateLogger(string categoryName) => new Tail(this, categoryName);

    public void Dispose()
    {
    }

    #endregion

    #region Private Methods

    private void Keep(string line)
    {
        lock (lines)
        {
            lines.Enqueue(line);
            while (lines.Count > Capacity)
            {
                lines.Dequeue();
            }
        }
    }

    #endregion

    #region Types

    private sealed class Tail(LogTail owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = $"{logLevel} {category}: {formatter(state, exception)}";
            owner.Keep(exception is null ? line : $"{line} {exception.GetType().Name}: {exception.Message}");
        }
    }

    #endregion
}
