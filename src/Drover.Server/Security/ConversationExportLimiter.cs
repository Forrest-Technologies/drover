namespace Drover.Server.Security;

/// <summary>How many conversation exports one person or the whole server may stream at once.</summary>
public sealed class ConversationExportLimitOptions
{
    public int MaxConcurrentPerUser { get; set; } = 2;

    public int MaxConcurrentTotal { get; set; } = 8;
}

/// <summary>
/// Bounds concurrent conversation exports per user and server-wide so exporting cannot tie up request workers or
/// database connections.
/// </summary>
public sealed class ConversationExportLimiter : IDisposable
{
    #region Private Fields

    private readonly PartitionedRateLimiter<Guid> perUser;

    private readonly ConcurrencyLimiter total;

    #endregion

    #region Constructors

    public ConversationExportLimiter(IOptions<ConversationExportLimitOptions> options)
    {
        var limits = options.Value;
        var userLimit = Math.Max(1, limits.MaxConcurrentPerUser);
        var totalLimit = Math.Max(1, limits.MaxConcurrentTotal);

        perUser = PartitionedRateLimiter.Create<Guid, Guid>(userId =>
            RateLimitPartition.GetConcurrencyLimiter(
                userId,
                _ => new ConcurrencyLimiterOptions
                {
                    PermitLimit = userLimit,
                    QueueLimit = 0,
                }));

        total = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = totalLimit,
            QueueLimit = 0,
        });
    }

    #endregion

    #region Public Methods

    public IDisposable? TryAcquire(Guid userId)
    {
        var userLease = perUser.AttemptAcquire(userId);

        if (!userLease.IsAcquired)
        {
            userLease.Dispose();
            return null;
        }

        var totalLease = total.AttemptAcquire();

        if (!totalLease.IsAcquired)
        {
            totalLease.Dispose();
            userLease.Dispose();
            return null;
        }

        return new Lease(userLease, totalLease);
    }

    public void Dispose()
    {
        total.Dispose();
        perUser.Dispose();
    }

    #endregion

    #region Nested Types

    private sealed class Lease(RateLimitLease userLease, RateLimitLease totalLease) : IDisposable
    {
        #region Private Fields

        private int disposed;

        #endregion

        #region Public Methods

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            totalLease.Dispose();
            userLease.Dispose();
        }

        #endregion
    }

    #endregion
}
