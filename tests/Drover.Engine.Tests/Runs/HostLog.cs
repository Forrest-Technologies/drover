namespace Drover.Engine.Tests.Runs;

/// <summary>
/// What a host's run engine logged, kept in memory. A test waits on a line instead of guessing how long the host
/// needs to reach a state, and a stop that does not finish reports the last lines so the stuck wait is named.
/// </summary>
public sealed class HostLog : ILoggerProvider
{
    #region Private Fields

    private const int Capacity = 200;

    private readonly Lock gate = new();

    private readonly Queue<string> lines = [];

    private readonly List<(string Fragment, TaskCompletionSource Seen)> waiting = [];

    #endregion

    #region Public Methods

    /// <summary>Completes when a line containing <paramref name="fragment"/> has been logged, now or later.</summary>
    public Task Logged(string fragment)
    {
        lock (gate)
        {
            if (lines.Any(line => line.Contains(fragment, StringComparison.Ordinal)))
            {
                return Task.CompletedTask;
            }

            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting.Add((fragment, seen));
            return seen.Task;
        }
    }

    /// <summary>The kept lines, oldest first, one to a line.</summary>
    public string Tail()
    {
        lock (gate)
        {
            return string.Join(Environment.NewLine, lines);
        }
    }

    #endregion

    #region Interface Implementations

    public ILogger CreateLogger(string categoryName) => new Writer(this, categoryName);

    public void Dispose()
    {
    }

    #endregion

    #region Private Methods

    private void Keep(string line)
    {
        lock (gate)
        {
            lines.Enqueue(line);

            while (lines.Count > Capacity)
            {
                lines.Dequeue();
            }

            foreach (var entry in waiting.Where(entry => line.Contains(entry.Fragment, StringComparison.Ordinal)))
            {
                entry.Seen.TrySetResult();
            }

            waiting.RemoveAll(entry => entry.Seen.Task.IsCompleted);
        }
    }

    #endregion

    #region Types

    private sealed class Writer(HostLog owner, string category) : ILogger
    {
        #region Interface Implementations

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var line = $"{logLevel} {category}: {formatter(state, exception)}";
            owner.Keep(exception is null ? line : $"{line} ({exception.GetType().Name}: {exception.Message})");
        }

        #endregion
    }

    #endregion
}
