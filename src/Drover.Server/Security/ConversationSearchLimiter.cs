namespace Drover.Server.Security;

/// <summary>
/// Bounds how many conversation searches run at once per caller, per space, and across the server, so repeated
/// full-text searches cannot tie up database connections or CPU.
/// </summary>
public sealed class ConversationSearchLimiter
{
    #region Private Fields

    private readonly Lock gate = new();

    private readonly Dictionary<Guid, int> byUser = [];

    private readonly Dictionary<Guid, int> bySpace = [];

    private int active;

    #endregion

    #region Public Methods

    /// <summary>
    /// Reserves one search slot for <paramref name="userId"/> in <paramref name="spaceId"/>, or returns null when
    /// the caller, the space, or the server already holds as many active searches as allowed.
    /// </summary>
    public IDisposable? TryAcquire(Guid spaceId, Guid userId)
    {
        lock (gate)
        {
            var userCount = byUser.GetValueOrDefault(userId);
            var spaceCount = bySpace.GetValueOrDefault(spaceId);

            if (active >= MaxGlobal || userCount >= MaxPerUser || spaceCount >= MaxPerSpace)
            {
                return null;
            }

            active++;
            byUser[userId] = userCount + 1;
            bySpace[spaceId] = spaceCount + 1;
            return new Lease(this, spaceId, userId);
        }
    }

    #endregion

    #region Private Methods

    private void Release(Guid spaceId, Guid userId)
    {
        lock (gate)
        {
            active--;
            Decrement(byUser, userId);
            Decrement(bySpace, spaceId);
        }
    }

    private static void Decrement(Dictionary<Guid, int> counts, Guid key)
    {
        if (!counts.TryGetValue(key, out var count))
        {
            return;
        }

        if (count <= 1)
        {
            counts.Remove(key);
        }
        else
        {
            counts[key] = count - 1;
        }
    }

    #endregion

    #region Constants

    public const int MaxPerUser = 2;

    public const int MaxPerSpace = 4;

    public const int MaxGlobal = 16;

    #endregion

    #region Nested Types

    private sealed class Lease(ConversationSearchLimiter owner, Guid spaceId, Guid userId) : IDisposable
    {
        #region Private Fields

        private int released;

        #endregion

        #region Interface Implementations

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                owner.Release(spaceId, userId);
            }
        }

        #endregion
    }

    #endregion
}
