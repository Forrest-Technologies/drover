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

    /// <summary>The bot, tracked, so that a change to it is saved when the scope commits.</summary>
    public Task<Bot?> GetBotForUpdate(Guid id, CancellationToken cancellationToken) =>
        db.Bots.SingleOrDefaultAsync(bot => bot.Id == id && bot.Status != BotStatus.Removed, cancellationToken);

    public Task<bool> HandleExists(string handle, CancellationToken cancellationToken) =>
        db.Bots.AnyAsync(bot => bot.Handle == handle, cancellationToken);

    /// <summary>Whether a bot other than this one holds the handle.</summary>
    public Task<bool> HandleExists(string handle, Guid exceptBotId, CancellationToken cancellationToken) =>
        db.Bots.AnyAsync(bot => bot.Handle == handle && bot.Id != exceptBotId, cancellationToken);

    /// <summary>How many of these ids name a bot of this space that has not been removed.</summary>
    public Task<int> CountBots(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.Bots.CountAsync(bot => ids.Contains(bot.Id) && bot.Status != BotStatus.Removed, cancellationToken);

    public Task<bool> FileExists(Guid fileId, CancellationToken cancellationToken) =>
        db.Files.AnyAsync(file => file.Id == fileId, cancellationToken);

    /// <summary>The bots this bot's sandbox is shared with. A removed bot shares nothing, whatever rows remain.</summary>
    public Task<List<Guid>> ListSandboxShares(Guid botId, CancellationToken cancellationToken) =>
        db.BotSandboxShares.AsNoTracking()
            .Where(share => share.BotId == botId)
            .Where(share => db.Bots.Any(bot => bot.Id == share.SharedWithBotId && bot.Status != BotStatus.Removed))
            .OrderBy(share => share.SharedWithBotId)
            .Select(share => share.SharedWithBotId)
            .ToListAsync(cancellationToken);

    /// <summary>Ends every sandbox share the bot gives or was given. A removed bot has no sandbox to share.</summary>
    public async Task RemoveSandboxSharesOf(Guid botId, CancellationToken cancellationToken) =>
        db.BotSandboxShares.RemoveRange(
            await db.BotSandboxShares.Where(share => share.BotId == botId || share.SharedWithBotId == botId).ToListAsync(cancellationToken));

    /// <summary>Makes the bots a sandbox is shared with exactly these, adding and removing only what differs.</summary>
    public async Task ReplaceSandboxShares(Guid botId, IReadOnlyCollection<Guid> sharedWith, CancellationToken cancellationToken)
    {
        var stored = await db.BotSandboxShares.Where(share => share.BotId == botId).ToListAsync(cancellationToken);
        db.BotSandboxShares.RemoveRange(stored.Where(share => !sharedWith.Contains(share.SharedWithBotId)));

        foreach (var id in sharedWith.Where(id => stored.TrueForAll(share => share.SharedWithBotId != id)))
        {
            db.BotSandboxShares.Add(new BotSandboxShare { BotId = botId, SharedWithBotId = id });
        }
    }

    /// <summary>Adds a bot at the top of the tree, with its own closure row. Without that row a bot gets no grants at all.</summary>
    public void AddRootBot(Bot bot)
    {
        db.Bots.Add(bot);
        db.BotAncestors.Add(new BotAncestor { AncestorBotId = bot.Id, DescendantBotId = bot.Id, Distance = 0 });
    }

    #endregion
}
