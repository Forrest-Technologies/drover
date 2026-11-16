namespace Drover.Data.Repositories;

/// <summary>A schedule that has come due, as <c>sys.claim_due_schedules</c> returns it: ids only.</summary>
public sealed record DueSchedule(Guid SpaceId, Guid ScheduleId, Guid OwnerUserId);

/// <summary>A bot's schedules, and the claim the scheduler makes on the ones that have come due.</summary>
public sealed class BotScheduleRepository(DroverDbContext db)
{
    #region Private Fields

    // Mixed into the advisory lock key so it cannot collide with another lock taken on the same space.
    private const long ScheduleLockSeed = 0x44524F56_45520006;

    #endregion

    #region Public Methods

    /// <summary>
    /// Makes additions of schedules to one space take turns until the transaction ends, so two additions count the
    /// space's schedules one after the other. Spaces never wait on each other.
    /// </summary>
    public Task LockSpaceSchedules(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended(sys.current_space()::text, {ScheduleLockSeed}))",
            cancellationToken);

    public Task<List<BotSchedule>> ListSchedules(Guid botId, CancellationToken cancellationToken) =>
        db.BotSchedules.AsNoTracking()
            .Where(schedule => schedule.BotId == botId)
            .OrderBy(schedule => schedule.Id)
            .ToListAsync(cancellationToken);

    /// <summary>How many schedules the space holds, across all of its bots. The space is the transaction's own.</summary>
    public Task<int> CountSchedules(CancellationToken cancellationToken) =>
        db.BotSchedules.CountAsync(cancellationToken);

    /// <summary>The cron expressions of the schedules with these ids that still exist, by id.</summary>
    public Task<Dictionary<Guid, string>> ListCrons(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.BotSchedules.AsNoTracking()
            .Where(schedule => ids.Contains(schedule.Id))
            .ToDictionaryAsync(schedule => schedule.Id, schedule => schedule.Cron, cancellationToken);

    /// <summary>The schedule, tracked, so that a change to it is saved when the scope commits.</summary>
    public Task<BotSchedule?> GetScheduleForUpdate(Guid botId, Guid id, CancellationToken cancellationToken) =>
        db.BotSchedules.SingleOrDefaultAsync(schedule => schedule.Id == id && schedule.BotId == botId, cancellationToken);

    /// <summary>The schedule, tracked and locked until the transaction ends, so two firings of it take turns.</summary>
    public async Task<BotSchedule?> LockSchedule(Guid id, CancellationToken cancellationToken) =>
        (await db.BotSchedules.FromSql($"SELECT * FROM bot_schedules WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken))
            .SingleOrDefault();

    /// <summary>
    /// Claims the enabled schedules, in every space, that were due at <paramref name="now"/>, and moves each one's
    /// <c>next_run_at</c> to <paramref name="claimUntil"/> in the same statement, so no other caller claims it again
    /// before then. This crosses spaces, so it goes through <c>sys.claim_due_schedules</c>, which returns ids only:
    /// the caller then reads each schedule inside its own space's tenant scope, where it writes the real next time.
    /// </summary>
    public Task<List<DueSchedule>> ClaimDue(DateTimeOffset now, DateTimeOffset claimUntil, int limit, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<DueSchedule>(
                $"""
                SELECT space_id, schedule_id, owner_user_id
                FROM sys.claim_due_schedules({now}, {claimUntil}, {limit})
                """)
            .ToListAsync(cancellationToken);

    public void Add(BotSchedule schedule) => db.BotSchedules.Add(schedule);

    public void Remove(BotSchedule schedule) => db.BotSchedules.Remove(schedule);

    #endregion
}
