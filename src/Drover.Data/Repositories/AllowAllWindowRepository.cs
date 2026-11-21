namespace Drover.Data.Repositories;

/// <summary>
/// Allow-all windows in the caller's tenant scope. The caller owns the scope and commits it; this class only reads
/// and tracks rows. The audience policy keeps one person's windows invisible to another run's principal.
/// </summary>
public sealed class AllowAllWindowRepository(DroverDbContext db)
{
    #region Private Fields

    // Mixed into the advisory lock key so it cannot collide with another lock taken on the same space.
    private const long WindowLockSeed = 0x44524F56_4552000B;

    #endregion

    #region Public Methods

    public void Add(AllowAllWindow window) => db.AllowAllWindows.Add(window);

    /// <summary>
    /// Holds one space's windows still until the transaction ends, so two requests that turn on Allow all for the
    /// same bot and person take turns and one replaces the other instead of both racing the unique index.
    /// </summary>
    public Task LockSpaceWindows(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended(sys.current_space()::text, {WindowLockSeed}))",
            cancellationToken);

    /// <summary>
    /// The open window for one bot and person, locked until the transaction ends. Every caller ends the row it
    /// reads, so the lock is what keeps two overlapping decisions from both ending it and writing two audit rows.
    /// </summary>
    public async Task<AllowAllWindow?> FindOpen(Guid botId, Guid audienceUserId, CancellationToken cancellationToken) =>
        (await db.AllowAllWindows.FromSql(
            $"""
            SELECT * FROM allow_all_windows
            WHERE bot_id = {botId} AND audience_user_id = {audienceUserId} AND ended_at IS NULL
            FOR UPDATE
            """).ToListAsync(cancellationToken)).SingleOrDefault();

    /// <summary>
    /// Ends a window only while it is still open, in one statement. True exactly when this call changed the row,
    /// so the caller writes one audit entry for the one ending that won.
    /// </summary>
    public async Task<bool> TryEnd(Guid id, DateTimeOffset endedAt, AllowAllWindowEndReason reason, CancellationToken cancellationToken) =>
        await db.AllowAllWindows
            .Where(window => window.Id == id && window.EndedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(window => window.EndedAt, endedAt)
                    .SetProperty(window => window.EndedReason, reason),
                cancellationToken) == 1;

    /// <summary>A window by id, open or ended, for the audit facts written after <see cref="TryEnd"/>.</summary>
    public Task<AllowAllWindow?> Find(Guid id, CancellationToken cancellationToken) =>
        db.AllowAllWindows.AsNoTracking().SingleOrDefaultAsync(window => window.Id == id, cancellationToken);

    /// <summary>An open window by id, locked for the caller that ends it.</summary>
    public async Task<AllowAllWindow?> FindOpenById(Guid id, CancellationToken cancellationToken) =>
        (await db.AllowAllWindows.FromSql(
            $"SELECT * FROM allow_all_windows WHERE id = {id} AND ended_at IS NULL FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault();

    /// <summary>Every open window of a bot, in id order so two callers cannot lock them in different orders.</summary>
    public Task<List<AllowAllWindow>> ListOpenForBot(Guid botId, CancellationToken cancellationToken) =>
        db.AllowAllWindows.FromSql(
            $"SELECT * FROM allow_all_windows WHERE bot_id = {botId} AND ended_at IS NULL ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);

    /// <summary>Every open window addressed to one person, in id order for the same reason.</summary>
    public Task<List<AllowAllWindow>> ListOpenForAudience(Guid audienceUserId, CancellationToken cancellationToken) =>
        db.AllowAllWindows.FromSql(
            $"SELECT * FROM allow_all_windows WHERE audience_user_id = {audienceUserId} AND ended_at IS NULL ORDER BY id FOR UPDATE")
            .ToListAsync(cancellationToken);

    #endregion
}
