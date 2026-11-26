namespace Drover.Data.Repositories;

/// <summary>An installed extension with the name and status of the bot it added, when the bot is still there.</summary>
public sealed record InstalledExtension(ExtensionInstall Install, string? BotName, BotStatus? BotStatus);

/// <summary>The extensions a space has imported.</summary>
public sealed class ExtensionRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(ExtensionInstall install) => db.ExtensionInstalls.Add(install);

    public Task<bool> IsInstalled(string packageId, CancellationToken cancellationToken) =>
        db.ExtensionInstalls.AnyAsync(install => install.PackageId == packageId, cancellationToken);

    /// <summary>The tracked row, locked until the transaction ends, so two people connecting one extension take turns.</summary>
    public async Task<ExtensionInstall?> GetInstallForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.ExtensionInstalls.FromSql($"SELECT * FROM extension_installs WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();

    /// <summary>Unlinks a connection that is about to be removed from every extension that made it.</summary>
    public Task<int> ForgetConnection(Guid connectionId, CancellationToken cancellationToken) =>
        db.ExtensionInstalls
            .Where(install => install.ToolConnectionId == connectionId)
            .ExecuteUpdateAsync(install => install.SetProperty(row => row.ToolConnectionId, (Guid?)null), cancellationToken);

    /// <summary>The space's extensions, the newest first.</summary>
    public Task<List<InstalledExtension>> ListInstalled(CancellationToken cancellationToken) =>
        ListInstalled(null, cancellationToken);

    /// <summary>One installed extension, or null when this space has none by that id.</summary>
    public async Task<InstalledExtension?> FindInstalled(Guid id, CancellationToken cancellationToken) =>
        (await ListInstalled(id, cancellationToken)).SingleOrDefault();

    /// <summary>The install that added a bot, or null when the bot was made another way.</summary>
    public Task<ExtensionInstall?> FindForBot(Guid botId, CancellationToken cancellationToken) =>
        db.ExtensionInstalls.AsNoTracking().SingleOrDefaultAsync(install => install.BotId == botId, cancellationToken);

    #endregion

    #region Private Methods

    private async Task<List<InstalledExtension>> ListInstalled(Guid? id, CancellationToken cancellationToken)
    {
        var rows = await db.ExtensionInstalls.AsNoTracking()
            .Where(install => id == null || install.Id == id)
            .GroupJoin(db.Bots.AsNoTracking(), install => install.BotId, bot => bot.Id, (install, bots) => new { install, bots })
            .SelectMany(found => found.bots.DefaultIfEmpty(), (found, bot) => new { found.install, bot })
            .OrderByDescending(found => found.install.InstalledAt)
            .ThenByDescending(found => found.install.Id)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(found => found.bot is { Status: not BotStatus.Removed } bot
            ? new InstalledExtension(found.install, bot.Name, bot.Status)
            : new InstalledExtension(found.install, null, null))];
    }

    #endregion
}
