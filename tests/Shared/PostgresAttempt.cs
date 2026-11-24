namespace Drover.Tests.Common;

/// <summary>
/// One attempt to find PostgreSQL, shared by every caller that is handed the same instance. The task is created
/// under a lock and awaited outside it, so callers that arrive together get the one result or the one failure;
/// nothing opens a second connection or starts a second container. A faulted task is rethrown to every later caller.
/// </summary>
public sealed class PostgresAttempt
{
    #region Private Fields

    private readonly object gate = new();

    private Task<string>? attempt;

    #endregion

    #region Public Methods

    public Task<string> Get(Func<Task<string>> create)
    {
        lock (gate)
        {
            return attempt ??= create();
        }
    }

    /// <summary>Lets a test start with a clean attempt without touching other tests' instances.</summary>
    public void Reset()
    {
        lock (gate)
        {
            attempt = null;
        }
    }

    #endregion
}
