namespace Drover.Data.Repositories;

/// <summary>The extensions a space has imported.</summary>
public sealed class ExtensionRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(ExtensionInstall install) => db.ExtensionInstalls.Add(install);

    public void Remove(ExtensionInstall install) => db.ExtensionInstalls.Remove(install);

    public Task<bool> IsInstalled(string packageId, CancellationToken cancellationToken) =>
        db.ExtensionInstalls.AnyAsync(install => install.PackageId == packageId, cancellationToken);

    /// <summary>The tracked row, locked until the transaction ends, so two people changing one extension take turns.</summary>
    public async Task<ExtensionInstall?> GetInstallForUpdate(Guid id, CancellationToken cancellationToken) =>
        (await db.ExtensionInstalls.FromSql($"SELECT * FROM extension_installs WHERE id = {id} FOR UPDATE").ToListAsync(cancellationToken)).SingleOrDefault();

    /// <summary>Unlinks a connection that is about to be removed from every extension that made it.</summary>
    public Task<int> ForgetConnection(Guid connectionId, CancellationToken cancellationToken) =>
        db.ExtensionInstalls
            .Where(install => install.ToolConnectionId == connectionId)
            .ExecuteUpdateAsync(install => install.SetProperty(row => row.ToolConnectionId, (Guid?)null), cancellationToken);

    /// <summary>The space's extensions, the newest first. A space holds few, and each is at most one small file.</summary>
    public Task<List<ExtensionInstall>> ListInstalled(CancellationToken cancellationToken) =>
        db.ExtensionInstalls.AsNoTracking()
            .OrderByDescending(install => install.InstalledAt)
            .ThenByDescending(install => install.Id)
            .ToListAsync(cancellationToken);

    /// <summary>One installed extension, or null when this space has none by that id.</summary>
    public Task<ExtensionInstall?> FindInstalled(Guid id, CancellationToken cancellationToken) =>
        db.ExtensionInstalls.AsNoTracking().SingleOrDefaultAsync(install => install.Id == id, cancellationToken);

    #endregion
}
