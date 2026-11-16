namespace Drover.Engine.Bots;

/// <summary>
/// One pass of the scheduler: claims the schedules that are due in every space and sends each one. The server's
/// scheduler loop calls <see cref="SendDue"/> every <see cref="Interval"/>; the status page reads
/// <see cref="LastPassAt"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Finding due schedules crosses spaces, so it goes through <c>sys.claim_due_schedules</c>, which returns ids
/// only and moves each schedule's <c>next_run_at</c> to the end of this pass's claim. Two servers never claim one
/// schedule for one due time.</item>
/// <item>Each schedule is then sent by <see cref="ScheduleFiring"/> inside its own space's tenant scope, as its owner.
/// One that fails does not stop the pass. Its claim runs out after <see cref="ClaimFor"/>, and it is due again then, so
/// a schedule whose send failed is tried again rather than lost.</item>
/// </list>
/// </remarks>
public sealed class ScheduleRunner(RunScopes scopes, TimeProvider time, ILogger<ScheduleRunner> logger)
{
    #region Private Fields

    /// <summary>The most schedules one claim takes. A pass claims again while a claim comes back full.</summary>
    private const int MaxPerClaim = 50;

    /// <summary>The most claims in one pass, so one pass cannot run on without end.</summary>
    private const int MaxClaimsPerPass = 20;

    private long lastPassTicks;

    #endregion

    #region Properties

    /// <summary>The time between two passes. Cron has nothing finer than a minute.</summary>
    public static TimeSpan Interval { get; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a claim holds a schedule. Sending one takes a short transaction, far less than this.</summary>
    public static TimeSpan ClaimFor { get; } = TimeSpan.FromMinutes(5);

    /// <summary>When this server last finished a pass, or null when it has not finished one since it started.</summary>
    public DateTimeOffset? LastPassAt =>
        Interlocked.Read(ref lastPassTicks) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    #endregion

    #region Public Methods

    /// <summary>Claims the due schedules and sends each. Returns how many were handled.</summary>
    public async Task<int> SendDue(CancellationToken cancellationToken)
    {
        var handled = 0;

        for (var claim = 0; claim < MaxClaimsPerPass; claim++)
        {
            var now = time.GetUtcNow();
            var claimUntil = ToDatabasePrecision(now + ClaimFor);
            var due = await scopes.WithoutTenant(
                services => services.GetRequiredService<BotScheduleRepository>().ClaimDue(now, claimUntil, MaxPerClaim, cancellationToken),
                cancellationToken);

            foreach (var entry in due)
            {
                handled += await Send(entry, claimUntil, cancellationToken) ? 1 : 0;
            }

            if (due.Count < MaxPerClaim)
            {
                break;
            }
        }

        Interlocked.Exchange(ref lastPassTicks, time.GetUtcNow().UtcTicks);
        return handled;
    }

    #endregion

    #region Private Methods

    private async Task<bool> Send(DueSchedule entry, DateTimeOffset claimUntil, CancellationToken cancellationToken)
    {
        try
        {
            ScheduleFire? fire = null;
            await scopes.For(entry.SpaceId, entry.OwnerUserId).InScope(async services =>
                fire = await services.GetRequiredService<ScheduleFiring>().Fire(entry.ScheduleId, claimUntil, cancellationToken));
            return fire is not null;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // One schedule does not end the pass; it is due again when its claim runs out. Only the kind of failure:
            // its text could hold the prompt.
            logger.LogWarning("Sending schedule {ScheduleId} failed; it is tried again when its claim runs out: {Failure}", entry.ScheduleId, failure.GetType().Name);
            return false;
        }
    }

    /// <summary>PostgreSQL keeps microseconds, so the claim is compared with what the database stored.</summary>
    private static DateTimeOffset ToDatabasePrecision(DateTimeOffset moment) =>
        new(moment.UtcTicks - (moment.UtcTicks % 10), TimeSpan.Zero);

    #endregion
}
