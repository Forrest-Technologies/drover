namespace Drover.Data.Repositories;

/// <summary>A run a worker leased through <c>sys.claim_runs</c>: ids only. The worker reads the row in the run's own tenant scope.</summary>
public sealed record ClaimedRun(Guid RunId, Guid SpaceId, Guid PrincipalUserId);

/// <summary>
/// One claim of one run: the worker that made it and the attempt number the claim gave the run. A worker name is
/// per process, so it cannot tell two claims by the same server apart; the attempt can, because taking over a
/// running run always raises it. Every write an execution makes names its fence, and a write whose fence no longer
/// matches the row changes nothing.
/// </summary>
public sealed record RunFence(string Worker, int Attempt);

/// <summary>The run is held by another claim now. The execution that learns this stops and writes nothing more.</summary>
public sealed class LeaseLostException(Guid runId) : Exception($"The lease on run {runId} belongs to another claim now.");

/// <summary>What a worker learns when it looks at its lease: whether it still holds it, and whether someone asked to stop.</summary>
public sealed record LeaseState(bool Held, bool CancelRequested);

/// <summary>
/// Runs as rows: queueing, leasing, cancelling, and usage. Every method runs inside a tenant scope the caller opened.
/// <see cref="Claim"/> is the one that crosses spaces; it goes through <c>sys.claim_runs</c> and returns ids only.
/// </summary>
public sealed class RunRepository(DroverDbContext db)
{
    #region Private Fields

    /// <summary>The channel a queued run is announced on. The payload is empty: a notification never carries tenant data.</summary>
    public const string WakeChannel = "drover_runs";

    #endregion

    #region Reads

    public Task<Run?> FindRun(Guid id, CancellationToken cancellationToken) =>
        db.Runs.AsNoTracking().SingleOrDefaultAsync(run => run.Id == id, cancellationToken);

    /// <summary>The tracked row, locked until the transaction ends, so a status change cannot race a claim or a cancel.</summary>
    public async Task<Run?> GetRunForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.Runs.FromSql($"SELECT * FROM runs WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();

    /// <summary>The root run still working on a thread, if any. A thread runs one root run at a time.</summary>
    public Task<Run?> FindActiveRun(Guid threadId, CancellationToken cancellationToken) =>
        db.Runs.AsNoTracking()
            .Where(run => run.ThreadId == threadId && run.ParentRunId == null
                && (run.Status == RunStatus.Queued || run.Status == RunStatus.Running || run.Status == RunStatus.Waiting))
            .OrderBy(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Whether a run of the bot is working right now: running, not queued and not waiting on a person.</summary>
    public Task<bool> IsBotWorking(Guid botId, CancellationToken cancellationToken) =>
        db.Runs.AnyAsync(run => run.BotId == botId && run.Status == RunStatus.Running, cancellationToken);

    /// <summary>For each of the threads that has one, the id of its active root run.</summary>
    public async Task<Dictionary<Guid, Guid>> ListActiveRunIds(IReadOnlyCollection<Guid> threadIds, CancellationToken cancellationToken)
    {
        var active = await db.Runs.AsNoTracking()
            .Where(run => threadIds.Contains(run.ThreadId) && run.ParentRunId == null
                && (run.Status == RunStatus.Queued || run.Status == RunStatus.Running || run.Status == RunStatus.Waiting))
            .Select(run => new { run.ThreadId, run.Id })
            .ToListAsync(cancellationToken);
        return active.GroupBy(run => run.ThreadId).ToDictionary(group => group.Key, group => group.Min(run => run.Id));
    }

    #endregion

    #region Writes

    public void Add(Run run) => db.Runs.Add(run);

    public void Add(UsageRecord usage) => db.UsageRecords.Add(usage);

    /// <summary>Wakes every server's workers when the transaction commits. PostgreSQL delivers nothing if it rolls back.</summary>
    public Task SignalWorkers(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT pg_notify({WakeChannel}, '')", cancellationToken);

    /// <summary>Asks the run and every run below it in its tree to stop, in one statement.</summary>
    public Task<int> RequestCancel(Guid runId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            WITH RECURSIVE tree AS (
              SELECT id FROM runs WHERE id = {runId}
              UNION ALL
              SELECT child.id FROM runs child JOIN tree ON child.parent_run_id = tree.id)
            UPDATE runs SET cancel_requested_at = now()
            FROM tree
            WHERE runs.id = tree.id AND runs.cancel_requested_at IS NULL
              AND runs.status IN ('queued', 'running', 'waiting')
            """,
            cancellationToken);

    #endregion

    #region Leases

    /// <summary>
    /// Leases runnable root runs across all spaces to this worker. The caller's transaction has no space set: the
    /// function is the bypass, it returns ids only, and the worker then opens a scope for each run's own space.
    /// </summary>
    public Task<List<ClaimedRun>> Claim(string worker, int limit, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<ClaimedRun>($"SELECT run_id, space_id, principal_user_id FROM sys.claim_runs({worker}, {limit})")
            .ToListAsync(cancellationToken);

    /// <summary>Whether this worker still holds the run, and whether a stop was requested.</summary>
    public async Task<LeaseState> ReadLease(Guid runId, RunFence fence, CancellationToken cancellationToken)
    {
        var run = await db.Runs.AsNoTracking()
            .Where(found => found.Id == runId)
            .Select(found => new { found.Status, found.LeaseOwner, found.Attempt, found.CancelRequestedAt })
            .SingleOrDefaultAsync(cancellationToken);
        return new LeaseState(
            run is { Status: RunStatus.Running } && run.LeaseOwner == fence.Worker && run.Attempt == fence.Attempt,
            run?.CancelRequestedAt is not null);
    }

    /// <summary>Pushes the lease out again. False when the run is no longer this worker's.</summary>
    public async Task<bool> RenewLease(Guid runId, RunFence fence, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE runs SET lease_expires_at = now() + interval '45 seconds'
            WHERE id = {runId} AND lease_owner = {fence.Worker} AND attempt = {fence.Attempt} AND status = 'running'
            """,
            cancellationToken) == 1;

    /// <summary>Gives the lease up at once, so another worker can take the run without waiting for it to expire.</summary>
    public Task ReleaseLease(Guid runId, RunFence fence, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            UPDATE runs SET lease_expires_at = now() - interval '1 second'
            WHERE id = {runId} AND lease_owner = {fence.Worker} AND attempt = {fence.Attempt} AND status = 'running'
            """,
            cancellationToken);

    #endregion
}
