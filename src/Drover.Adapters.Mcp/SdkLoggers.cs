namespace Drover.Adapters.Mcp;

/// <summary>
/// The loggers the protocol library writes to, with its errors told as warnings. Every failure it logs also reaches
/// this adapter as an exception, which Drover records on the connection and shows: a tool server that is down or
/// refuses is that server's state, not a failure of Drover. Nothing is dropped; only the level changes.
/// </summary>
internal sealed class SdkLoggers(ILoggerFactory inner) : ILoggerFactory
{
    #region Interface Implementations

    public ILogger CreateLogger(string categoryName) => new Lowered(inner.CreateLogger(categoryName));

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    // The factory belongs to the host; this wrapper owns nothing.
    public void Dispose()
    {
    }

    #endregion

    #region Nested Types

    private sealed class Lowered(ILogger inner) : ILogger
    {
        #region Interface Implementations

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(Lower(logLevel));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            inner.Log(Lower(logLevel), eventId, state, exception, formatter);

        #endregion

        #region Private Methods

        private static LogLevel Lower(LogLevel level) => level is LogLevel.Error ? LogLevel.Warning : level;

        #endregion
    }

    #endregion
}
