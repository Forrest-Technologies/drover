namespace Drover.Engine.Accounts;

/// <summary>
/// Tells the open streams of one person, in this server, that some of their sessions just ended, so each stream
/// checks its own session at once instead of at its next interval. It carries no decision: a stream that hears
/// it still asks the database whether its session is there. Other servers on the same database find out at their
/// streams' next check.
/// </summary>
public sealed class SessionRevocations
{
    #region Private Fields

    private readonly ConcurrentDictionary<Guid, ImmutableList<Action>> listeners = new();

    #endregion

    #region Public Methods

    /// <summary>Calls <paramref name="heard"/> each time a session of the person ends, until disposed.</summary>
    public IDisposable Listen(Guid userId, Action heard)
    {
        listeners.AddOrUpdate(userId, _ => [heard], (_, existing) => existing.Add(heard));
        return new Listening(() => Remove(userId, heard));
    }

    /// <summary>Called after the transaction that removed sessions of the person has committed.</summary>
    public void Announce(Guid userId)
    {
        if (listeners.TryGetValue(userId, out var heard))
        {
            foreach (var listener in heard)
            {
                listener();
            }
        }
    }

    #endregion

    #region Private Methods

    private void Remove(Guid userId, Action heard)
    {
        while (listeners.TryGetValue(userId, out var existing))
        {
            var remaining = existing.Remove(heard);
            var replaced = remaining.IsEmpty
                ? listeners.TryRemove(new KeyValuePair<Guid, ImmutableList<Action>>(userId, existing))
                : listeners.TryUpdate(userId, remaining, existing);

            if (replaced)
            {
                return;
            }
        }
    }

    #endregion

    #region Nested Types

    private sealed class Listening(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }

    #endregion
}
