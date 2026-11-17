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

    /// <summary>The space's extensions, the newest first.</summary>
    public async Task<List<InstalledExtension>> ListInstalled(CancellationToken cancellationToken)
    {
        var rows = await db.ExtensionInstalls.AsNoTracking()
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
