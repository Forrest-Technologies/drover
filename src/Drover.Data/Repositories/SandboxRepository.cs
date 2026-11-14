namespace Drover.Data.Repositories;

/// <summary>
/// Sandbox rows: at most one live row per owner bot, which the partial unique index <see cref="OneLivePerOwner"/>
/// guarantees, so two first uses at once make one sandbox. Runs inside the caller's tenant scope.
/// </summary>
public sealed class SandboxRepository(DroverDbContext db)
{
    #region Public Methods

    /// <summary>The owner's sandbox that is being created, running, or paused, if it has one.</summary>
    public Task<Sandbox?> FindLiveForOwner(Guid ownerBotId, CancellationToken cancellationToken) =>
        db.Sandboxes.AsNoTracking()
            .SingleOrDefaultAsync(
                sandbox => sandbox.OwnerBotId == ownerBotId
                    && (sandbox.State == SandboxState.Creating || sandbox.State == SandboxState.Running || sandbox.State == SandboxState.Paused),
                cancellationToken);

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

    /// <summary>The owner's retiring sandboxes whose time to be destroyed has come.</summary>
    public async Task<IReadOnlyList<Sandbox>> ListDueForOwner(Guid ownerBotId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Sandboxes.AsNoTracking()
            .Where(sandbox => sandbox.OwnerBotId == ownerBotId && sandbox.State == SandboxState.Retiring && sandbox.RetireAfter <= now)
            .ToListAsync(cancellationToken);

    /// <summary>Whether the owner has a sandbox taken out of use that is not destroyed yet.</summary>
    public Task<bool> HasRetiringForOwner(Guid ownerBotId, CancellationToken cancellationToken) =>
        db.Sandboxes.AnyAsync(sandbox => sandbox.OwnerBotId == ownerBotId && sandbox.State == SandboxState.Retiring, cancellationToken);

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
