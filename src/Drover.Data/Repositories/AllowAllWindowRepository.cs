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

    /// <summary>The open window for one bot and person, if any. Tracked, so the service can end it.</summary>
    public Task<AllowAllWindow?> FindOpen(Guid botId, Guid audienceUserId, CancellationToken cancellationToken) =>
        db.AllowAllWindows.SingleOrDefaultAsync(
            window => window.BotId == botId && window.AudienceUserId == audienceUserId && window.EndedAt == null,
            cancellationToken);

    /// <summary>An open window by id, for the API's Ask again.</summary>
    public Task<AllowAllWindow?> FindOpenById(Guid id, CancellationToken cancellationToken) =>
        db.AllowAllWindows.SingleOrDefaultAsync(window => window.Id == id && window.EndedAt == null, cancellationToken);

    /// <summary>Every open window of a bot, whatever person it acts for.</summary>
    public Task<List<AllowAllWindow>> ListOpenForBot(Guid botId, CancellationToken cancellationToken) =>
        db.AllowAllWindows
            .Where(window => window.BotId == botId && window.EndedAt == null)
            .ToListAsync(cancellationToken);

    /// <summary>Every open window addressed to one person, on any bot of the space.</summary>
    public Task<List<AllowAllWindow>> ListOpenForAudience(Guid audienceUserId, CancellationToken cancellationToken) =>
        db.AllowAllWindows
            .Where(window => window.AudienceUserId == audienceUserId && window.EndedAt == null)
            .ToListAsync(cancellationToken);

    #endregion
}
