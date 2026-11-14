namespace Drover.Engine.Sandboxes;

/// <summary>
/// Clears up sandboxes that no bot will come back for: a retired one is paused at the provider at once and destroyed
/// when its retention is over, and a row left <see cref="SandboxState.Creating"/> by an interrupted creation is taken
/// out of use, so its bot can get a new one, and cleared on the next pass.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Finding them crosses spaces, so it goes through <c>sys.sandboxes_to_sweep</c>, which returns ids only. Each
/// one is then handled inside its own space's tenant scope, as the space itself, with no person set. No transaction
/// is open while the provider is asked.</item>
/// <item>Pausing and destroying need no access token, so the sweep never reveals one: the handle it builds carries
/// none. The token is deleted from the vault when the sandbox is destroyed.</item>
/// <item>One sandbox the provider cannot destroy now does not stop the pass, and is not tried again for
/// <see cref="RetryAfter"/>. One it cannot pause is left to pause on its own when its idle time runs out.</item>
/// <item>A row left creating holds no provider id, and the provider interface has no way to look a sandbox up by the
/// idempotency key without making one. The row is first retired, due at once, so a creation still finishing records
/// its provider id on it; the next pass destroys whatever that id names, or marks the row destroyed when there is
/// none.</item>
/// </list>
/// </remarks>
public sealed class SandboxSweep(RunScopes scopes, ISandboxProvider provider, TimeProvider time, ILogger<SandboxSweep> logger)
{
    #region Properties

    /// <summary>
    /// How long a retired sandbox is kept, paused, so a person can still recover its files. The design makes this a
    /// space setting; until a space can change it, every space keeps seven days.
    /// </summary>
    public static TimeSpan Retention { get; } = TimeSpan.FromDays(7);

    /// <summary>How long a row may stay creating before it counts as interrupted. A creation takes seconds.</summary>
    public static TimeSpan StuckAfter { get; } = TimeSpan.FromMinutes(10);

    /// <summary>How long a sandbox the provider could not destroy waits before it is tried again.</summary>
    public static TimeSpan RetryAfter { get; } = TimeSpan.FromHours(1);

    /// <summary>The time between two passes.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromMinutes(1);

    #endregion

    #region Public Methods

    /// <summary>Looks once for sandboxes to clear up and handles each. Returns how many it changed.</summary>
    public async Task<int> Sweep(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var found = await scopes.WithoutTenant(
            services => services.GetRequiredService<SandboxRepository>().ListToSweep(now, now - StuckAfter, MaxPerPass, cancellationToken),
            cancellationToken);
        var changed = 0;

        foreach (var entry in found)
        {
            try
            {
                changed += await Handle(entry, now, cancellationToken) ? 1 : 0;
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // One sandbox does not end the pass. Only the kind of failure: its text could hold a provider's words.
                logger.LogWarning("Sweeping sandbox {SandboxId} failed: {Failure}", entry.SandboxId, failure.GetType().Name);
            }
        }

        return changed;
    }

    #endregion

    #region Private Methods

    private async Task<bool> Handle(SandboxToSweep entry, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tenant = scopes.For(entry.SpaceId, Audience.Everyone);
        var row = await tenant.Transact(
            services => services.GetRequiredService<SandboxRepository>().GetSandboxForUpdate(entry.SandboxId, cancellationToken),
            cancellationToken);

        return row switch
        {
            { State: SandboxState.Creating } => await TakeOutOfUse(tenant, row, now, cancellationToken),
            { State: SandboxState.Retiring } when row.RetireAfter <= now => await Destroy(tenant, row, now, cancellationToken),
            { State: SandboxState.Retiring } when row.ExpiresAt > now => await Pause(tenant, row, now, cancellationToken),
            _ => false,
        };
    }

    /// <summary>An interrupted creation: retired, due now, which frees its owner to get a new sandbox.</summary>
    private async Task<bool> TakeOutOfUse(RunTenant tenant, Sandbox found, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var changed = await Change(tenant, found.Id, SandboxState.Creating, row =>
        {
            row.State = SandboxState.Retiring;
            row.RetireAfter = now;
        }, cancellationToken);

        if (changed)
        {
            logger.LogInformation("Sandbox {SandboxId} of bot {BotId} was left creating; it is retired", found.Id, found.OwnerBotId);
        }

        return changed;
    }

    /// <summary>Destroys the sandbox at the provider, then deletes its token and marks the row destroyed.</summary>
    private async Task<bool> Destroy(RunTenant tenant, Sandbox found, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (found.ProviderSandboxId.Length > 0)
        {
            try
            {
                await provider.Destroy(HandleOf(found), cancellationToken);
            }
            catch (SandboxProviderException failure)
            {
                logger.LogWarning("Retired sandbox {SandboxId} could not be destroyed yet: {Failure}", found.Id, failure.Failure);
                await Change(tenant, found.Id, SandboxState.Retiring, row => row.RetireAfter = now + RetryAfter, cancellationToken);
                return false;
            }
        }

        var destroyed = await Forget(tenant, found.Id, cancellationToken);

        if (destroyed)
        {
            logger.LogInformation("Destroyed retired sandbox {SandboxId} of bot {BotId}", found.Id, found.OwnerBotId);
        }

        return destroyed;
    }

    /// <summary>
    /// Pauses a retired sandbox that may still be running, and notes that it is paused. One the provider no longer
    /// has is marked destroyed; one it cannot pause now pauses on its own when its idle time runs out.
    /// </summary>
    private async Task<bool> Pause(RunTenant tenant, Sandbox found, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (found.ProviderSandboxId.Length > 0)
        {
            try
            {
                await provider.Pause(HandleOf(found), cancellationToken);
            }
            catch (SandboxProviderException failure) when (failure.Failure is SandboxFailure.NotFound)
            {
                logger.LogInformation("Retired sandbox {SandboxId} is already gone at the provider", found.Id);
                return await Forget(tenant, found.Id, cancellationToken);
            }
            catch (SandboxProviderException failure)
            {
                logger.LogWarning("Retired sandbox {SandboxId} could not be paused; it pauses when its idle time runs out: {Failure}", found.Id, failure.Failure);
            }
        }

        return await Change(tenant, found.Id, SandboxState.Retiring, row => row.ExpiresAt = now, cancellationToken);
    }

    /// <summary>Marks a retiring row destroyed and deletes its token, unless something else changed it first.</summary>
    private Task<bool> Forget(RunTenant tenant, Guid sandboxId, CancellationToken cancellationToken) =>
        tenant.Transact(
            async services =>
            {
                var row = await services.GetRequiredService<SandboxRepository>().GetSandboxForUpdate(sandboxId, cancellationToken);

                if (row is not { State: SandboxState.Retiring })
                {
                    return false;
                }

                if (row.AccessSecretId is { } secretId)
                {
                    await services.GetRequiredService<ISecretVault>().Delete(secretId, cancellationToken);
                    row.AccessSecretId = null;
                }

                row.State = SandboxState.Destroyed;
                return true;
            },
            cancellationToken);

    /// <summary>Changes the row, locked, only while it is still in the state it was found in.</summary>
    private static Task<bool> Change(RunTenant tenant, Guid sandboxId, SandboxState expected, Action<Sandbox> change, CancellationToken cancellationToken) =>
        tenant.Transact(
            async services =>
            {
                var row = await services.GetRequiredService<SandboxRepository>().GetSandboxForUpdate(sandboxId, cancellationToken);

                if (row is null || row.State != expected)
                {
                    return false;
                }

                change(row);
                return true;
            },
            cancellationToken);

    /// <summary>A handle for the provider's control plane. Pausing and destroying need no access token, so it has none.</summary>
    private static SandboxHandle HandleOf(Sandbox sandbox) =>
        new(sandbox.ProviderSandboxId, sandbox.SpaceId, sandbox.OwnerBotId, string.Empty);

    #endregion

    #region Constants

    /// <summary>The most sandboxes one pass handles. The rest wait for the next pass.</summary>
    private const int MaxPerPass = 50;

    #endregion
}
