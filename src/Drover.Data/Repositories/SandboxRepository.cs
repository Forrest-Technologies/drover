namespace Drover.Data.Repositories;

/// <summary>
/// Sandbox rows: at most one live row per owner bot, which the partial unique index <see cref="OneLivePerOwner"/>
/// guarantees, so two first uses at once make one sandbox. Runs inside the caller's tenant scope.
/// </summary>
public sealed class SandboxRepository(DroverDbContext db)
{
    #region Private Fields

    // Mixed into the advisory lock key so a copy fence cannot collide with another lock on the same space.
    private const long CopyLockSeed = 0x44524F56_45520007;

    #endregion

    #region Public Methods

    /// <summary>The owner's sandbox that is being created, running, or paused, if it has one.</summary>
    public Task<Sandbox?> FindLiveForOwner(Guid ownerBotId, CancellationToken cancellationToken) =>
        db.Sandboxes.AsNoTracking()
            .SingleOrDefaultAsync(
                sandbox => sandbox.OwnerBotId == ownerBotId
                    && (sandbox.State == SandboxState.Creating || sandbox.State == SandboxState.Running || sandbox.State == SandboxState.Paused),
                cancellationToken);

    /// <summary>The owner's newest sandbox in any state, live or gone, if it ever had one.</summary>
    public Task<Sandbox?> FindNewestForOwner(Guid ownerBotId, CancellationToken cancellationToken) =>
        db.Sandboxes.AsNoTracking()
            .Where(sandbox => sandbox.OwnerBotId == ownerBotId)
            .OrderByDescending(sandbox => sandbox.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The tracked row, locked until the transaction ends, so two calls that finish creating it take turns.</summary>
    public async Task<Sandbox?> GetSandboxForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.Sandboxes.FromSql($"SELECT * FROM sandboxes WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();

    public void Add(Sandbox sandbox) => db.Sandboxes.Add(sandbox);

    /// <summary>
    /// Takes the owner's live sandbox out of use: it becomes <see cref="SandboxState.Retiring"/>, to be destroyed from
    /// <paramref name="retireAfter"/>, and the owner's next use makes a new one. False when the owner has none.
    /// </summary>
    public async Task<bool> RetireLiveForOwner(Guid ownerBotId, DateTimeOffset retireAfter, CancellationToken cancellationToken) =>
        await db.Sandboxes
            .Where(sandbox => sandbox.OwnerBotId == ownerBotId
                && (sandbox.State == SandboxState.Creating || sandbox.State == SandboxState.Running || sandbox.State == SandboxState.Paused))
            .ExecuteUpdateAsync(
                update => update.SetProperty(sandbox => sandbox.State, SandboxState.Retiring).SetProperty(sandbox => sandbox.RetireAfter, retireAfter),
                cancellationToken) > 0;

    /// <summary>
    /// Makes a copy into a sandbox and a narrowing of who may see one take turns, until this transaction ends.
    /// A copy holds it while it reads the file's audience again and checks that the sandbox row still exists. A narrowing holds it from
    /// the retirement through the audience change. Spaces never wait on each other.
    /// </summary>
    public Task LockCopies(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended(sys.current_space()::text, {CopyLockSeed}))",
            cancellationToken);

    /// <summary>
    /// Takes every live sandbox of the space out of use, due from <paramref name="retireAfter"/>.
    /// Holds <see cref="LockCopies"/> first. The next use destroys each one and makes a new one, so a private write
    /// does not outlive a narrowing of who may see it. How many were retired.
    /// </summary>
    public async Task<int> RetireLive(DateTimeOffset retireAfter, CancellationToken cancellationToken)
    {
        await LockCopies(cancellationToken);
        return await db.Sandboxes
            .Where(sandbox => sandbox.State == SandboxState.Creating
                || sandbox.State == SandboxState.Running
                || sandbox.State == SandboxState.Paused)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(sandbox => sandbox.State, SandboxState.Retiring)
                    .SetProperty(sandbox => sandbox.RetireAfter, retireAfter),
                cancellationToken);
    }

    /// <summary>The owner's retiring sandboxes whose time to be destroyed has come.</summary>
    public async Task<IReadOnlyList<Sandbox>> ListDueForOwner(Guid ownerBotId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Sandboxes.AsNoTracking()
            .Where(sandbox => sandbox.OwnerBotId == ownerBotId && sandbox.State == SandboxState.Retiring && sandbox.RetireAfter <= now)
            .ToListAsync(cancellationToken);

    /// <summary>Whether the owner has a sandbox taken out of use that is not destroyed yet.</summary>
    public Task<bool> HasRetiringForOwner(Guid ownerBotId, CancellationToken cancellationToken) =>
        db.Sandboxes.AnyAsync(sandbox => sandbox.OwnerBotId == ownerBotId && sandbox.State == SandboxState.Retiring, cancellationToken);

    /// <summary>
    /// The sandboxes, in every space, that the sweep must act on: retiring ones that are due, or that may still be
    /// running, and ones left creating since before <paramref name="stuckBefore"/>. This crosses spaces, so it goes
    /// through <c>sys.sandboxes_to_sweep</c>, which returns ids only: the caller then handles each one inside that
    /// space's own tenant scope.
    /// </summary>
    public Task<List<SandboxToSweep>> ListToSweep(DateTimeOffset now, DateTimeOffset stuckBefore, int limit, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<SandboxToSweep>(
                $"""
                SELECT space_id, sandbox_id
                FROM sys.sandboxes_to_sweep({now}, {stuckBefore}, {limit})
                """)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// True when a save failed because the owner already has a live sandbox, added by another call at the same time.
    /// The transaction is lost, so the caller starts a new one and reads that row.
    /// </summary>
    public static bool IsAlreadyLive(Exception failure) =>
        failure is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: OneLivePerOwner } };

    #endregion

    #region Constants

    /// <summary>The partial unique index on the owner of every sandbox that is not retiring or destroyed.</summary>
    public const string OneLivePerOwner = "ux_sandboxes_one_live_per_owner";

    #endregion
}
