namespace Drover.Engine.Memory;

/// <summary>
/// Retries pending conversation and person forget requests against the configured memory service once an hour until
/// the provider accepts. Cross-space discovery returns ids only through <c>sys.memory_forget_requests_due</c>; each
/// request is then read and completed inside its own space's tenant scope, with no transaction open during the
/// provider call.
/// </summary>
public sealed class MemoryForgetRetry(
    RunScopes scopes,
    MemoryFeed feed,
    TimeProvider time,
    ILogger<MemoryForgetRetry> logger)
{
    #region Private Fields

    /// <summary>How often the server retries refused forget requests.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>How long a failed attempt waits before another pass retries it.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    /// <summary>The most forget requests one hourly pass attempts.</summary>
    public const int MaxPerPass = 100;

    #endregion

    #region Public Methods

    /// <summary>Processes due forget requests once and returns how many the provider accepted.</summary>
    public async Task<int> RetryDue(CancellationToken cancellationToken)
    {
        if (!feed.IsActive)
        {
            return 0;
        }

        var now = time.GetUtcNow();
        var due = await scopes.WithoutTenant(
            services => services.GetRequiredService<MemoryForgetRepository>().ListDue(now - RetryAfter, MaxPerPass, cancellationToken),
            cancellationToken);
        var forgotten = 0;

        foreach (var item in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await ProcessOne(item, cancellationToken))
                {
                    forgotten++;
                }
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                logger.LogWarning(
                    "Retrying memory forget request {RequestId} in space {SpaceId} failed: {Failure}",
                    item.RequestId,
                    item.SpaceId,
                    failure.GetType().Name);
            }
        }

        return forgotten;
    }

    #endregion

    #region Private Methods

    private async Task<bool> ProcessOne(MemoryForgetDue item, CancellationToken cancellationToken)
    {
        var loaded = await scopes.For(item.SpaceId, Audience.Everyone).Transact(
            async services =>
            {
                var repository = services.GetRequiredService<MemoryForgetRepository>();

                if (await repository.GetForUpdate(item.RequestId, cancellationToken) is not { } request)
                {
                    return ((MemoryForgetRequest Request, EffectivePolicy Policy)?)null;
                }

                request.AttemptedAt = time.GetUtcNow();
                var policy = await services.GetRequiredService<PolicyService>().GetEffective(item.SpaceId, cancellationToken);
                return (request, policy);
            },
            cancellationToken);

        return loaded is { } pending && await feed.TryForget(scopes, pending.Request, pending.Policy, cancellationToken);
    }

    #endregion
}
