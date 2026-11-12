namespace Drover.Data.Repositories;

/// <summary>Bots as rows, with the closure rows that place each one in the tree. Runs inside the caller's tenant scope.</summary>
public sealed class BotRepository(DroverDbContext db)
{
    #region Public Methods

    public Task<Bot?> FindBot(Guid id, CancellationToken cancellationToken) =>
        db.Bots.AsNoTracking().SingleOrDefaultAsync(bot => bot.Id == id, cancellationToken);

    /// <summary>Every bot of the space that has not been removed, by name.</summary>
    public Task<List<Bot>> ListBots(CancellationToken cancellationToken) =>
        db.Bots.AsNoTracking()
            .Where(bot => bot.Status != BotStatus.Removed)
            .OrderBy(bot => bot.Name)
            .ThenBy(bot => bot.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> HandleExists(string handle, CancellationToken cancellationToken) =>
        db.Bots.AnyAsync(bot => bot.Handle == handle, cancellationToken);

    /// <summary>Adds a bot at the top of the tree, with its own closure row. Without that row a bot gets no grants at all.</summary>
    public void AddRootBot(Bot bot)
    {
        db.Bots.Add(bot);
        db.BotAncestors.Add(new BotAncestor { AncestorBotId = bot.Id, DescendantBotId = bot.Id, Distance = 0 });
    }

    #endregion
}
