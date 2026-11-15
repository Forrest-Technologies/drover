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

    /// <summary>The bot that holds this handle, if it has not been removed.</summary>
    public Task<Bot?> FindBotByHandle(string handle, CancellationToken cancellationToken) =>
        db.Bots.AsNoTracking().SingleOrDefaultAsync(bot => bot.Handle == handle && bot.Status != BotStatus.Removed, cancellationToken);

    /// <summary>
    /// The bot and every bot above it that has not been removed, each with its distance and sandbox policy: what
    /// decides which sandbox the bot works in.
    /// </summary>
    public async Task<List<SandboxPathEntry>> ListSandboxPath(Guid botId, CancellationToken cancellationToken)
    {
        var path = await db.BotAncestors.AsNoTracking()
            .Where(row => row.DescendantBotId == botId)
            .Join(
                db.Bots.Where(bot => bot.Status != BotStatus.Removed),
                row => row.AncestorBotId,
                bot => bot.Id,
                (row, bot) => new { bot.Id, row.Distance, bot.SandboxPolicy })
            .OrderBy(entry => entry.Distance)
            .ToListAsync(cancellationToken);
        return [.. path.Select(entry => new SandboxPathEntry(entry.Id, entry.Distance, entry.SandboxPolicy))];
    }

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

    /// <summary>
    /// The bot, tracked and locked until the transaction ends, or null when there is none or it was removed. Adding a
    /// bot beneath it takes this lock first, so two additions count its children one after the other.
    /// </summary>
    public async Task<Bot?> LockBot(Guid id, CancellationToken cancellationToken) =>
        (await db.Bots.FromSql($"SELECT * FROM bots WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken))
            .SingleOrDefault(bot => bot.Status != BotStatus.Removed);

    /// <summary>How many bots directly beneath this one have not been removed.</summary>
    public Task<int> CountChildren(Guid parentId, CancellationToken cancellationToken) =>
        db.Bots.CountAsync(bot => bot.ParentBotId == parentId && bot.Status != BotStatus.Removed, cancellationToken);

    /// <summary>The bot and every bot above it, read through the closure table.</summary>
    public Task<List<Bot>> ListSelfAndAncestors(Guid botId, CancellationToken cancellationToken) =>
        db.Bots.AsNoTracking()
            .Where(bot => db.BotAncestors.Any(row => row.DescendantBotId == botId && row.AncestorBotId == bot.Id))
            .OrderBy(bot => bot.Depth)
            .ToListAsync(cancellationToken);

    /// <summary>Every bot beneath this one, at any depth, that has not been removed: nearest first, then by name.</summary>
    public Task<List<Bot>> ListDescendants(Guid botId, CancellationToken cancellationToken) =>
        db.Bots.AsNoTracking()
            .Where(bot => bot.Status != BotStatus.Removed)
            .Where(bot => db.BotAncestors.Any(row => row.AncestorBotId == botId && row.DescendantBotId == bot.Id && row.Distance > 0))
            .OrderBy(bot => bot.Depth)
            .ThenBy(bot => bot.Name)
            .ThenBy(bot => bot.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Whether one bot is beneath another, at any depth. A bot is never beneath itself.</summary>
    public Task<bool> IsBeneath(Guid botId, Guid ancestorBotId, CancellationToken cancellationToken) =>
        db.BotAncestors.AnyAsync(
            row => row.DescendantBotId == botId && row.AncestorBotId == ancestorBotId && row.Distance > 0,
            cancellationToken);

    /// <summary>Adds a bot at the top of the tree, with its own closure row. Without that row a bot gets no grants at all.</summary>
    public void AddRootBot(Bot bot)
    {
        db.Bots.Add(bot);
        db.BotAncestors.Add(new BotAncestor { AncestorBotId = bot.Id, DescendantBotId = bot.Id, Distance = 0 });
    }

    /// <summary>
    /// Adds a bot beneath its <see cref="Bot.ParentBotId"/>, with its own closure row and one to every bot above it,
    /// each a step further than that bot is from the parent.
    /// </summary>
    public async Task AddChildBot(Bot bot, CancellationToken cancellationToken)
    {
        var above = await db.BotAncestors.AsNoTracking()
            .Where(row => row.DescendantBotId == bot.ParentBotId)
            .ToListAsync(cancellationToken);

        db.Bots.Add(bot);
        db.BotAncestors.Add(new BotAncestor { AncestorBotId = bot.Id, DescendantBotId = bot.Id, Distance = 0 });

        foreach (var row in above)
        {
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = row.AncestorBotId, DescendantBotId = bot.Id, Distance = row.Distance + 1 });
        }
    }

    #endregion
}
