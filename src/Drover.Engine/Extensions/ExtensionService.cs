namespace Drover.Engine.Extensions;

/// <summary>The result of reading or importing an extension: what came of it, or the sentence that says why not.</summary>
public sealed record ExtensionChange<T>(T? Value, string? Problem)
    where T : class;

/// <summary>
/// An installed extension as the screen shows it: the row, the file it keeps, and how far its tool server's setup
/// has come. The package is null when the kept file can no longer be read; the row can still be removed.
/// </summary>
public sealed record ExtensionState(ExtensionInstall Install, ExtensionPackage? Package, ExtensionConnectionSummary? Connection);

/// <summary>
/// Extensions of the current space: reading a file for a person to review, importing it, and listing what is
/// installed and what it offers. An extension holds data and never grants or applies anything: its tool server is
/// connected by a person, its themes are chosen by each member, and its rules are added to a bot by a person. The
/// caller, an endpoint, owns the tenant scope.
/// </summary>
public sealed class ExtensionService(
    ExtensionRepository extensions,
    ToolConnectionRepository toolConnections,
    PolicyService policy,
    AuditLog audit,
    TimeProvider time,
    ILogger<ExtensionService> logger)
{
    #region Public Methods

    /// <summary>What the file would add, for a person to read first. Nothing is stored and nothing is contacted.</summary>
    public async Task<ExtensionChange<ExtensionPreview>> Preview(string? text, CancellationToken cancellationToken)
    {
        var reading = ExtensionPackages.Read(text);

        if (reading.Package is not { } package)
        {
            return new(null, reading.Problem);
        }

        return new(
            new ExtensionPreview(package.Id, package.Name.Trim(), package.Description)
            {
                Connection = RecipeOf(package)?.ToPreview(Defaults(package), policy.Deployment.AllowToolConnections),
                Parts = package.ToParts(),
                AlreadyInstalled = await extensions.IsInstalled(package.Id, cancellationToken),
            },
            null);
    }

    /// <summary>
    /// Keeps the file. Nothing is contacted and nothing changes for any bot or person: from here its tool server can
    /// be connected, its themes chosen, and its rules added to a bot, each by a person.
    /// </summary>
    public async Task<ExtensionChange<ExtensionState>> Import(string? text, Guid actorUserId, CancellationToken cancellationToken)
    {
        var reading = ExtensionPackages.Read(text);

        if (reading.Package is not { } package)
        {
            return new(null, reading.Problem);
        }

        if (await extensions.IsInstalled(package.Id, cancellationToken))
        {
            return new(null, $"{package.Name.Trim()} is already in this space.");
        }

        var offered = await ListThemes(cancellationToken);

        if (package.Themes().FirstOrDefault(theme => offered.Any(other => other.Id == theme.Id)) is { } taken)
        {
            return new(null, $"This space already has a theme with the id \"{taken.Id}\", from {offered.First(other => other.Id == taken.Id).Source}. Nothing was added.");
        }

        var install = new ExtensionInstall
        {
            PackageId = package.Id,
            Name = package.Name.Trim(),
            Description = package.Description,
            ContentHash = reading.ContentHash,
            Package = JsonElement.Parse(text!),
            InstalledByUserId = actorUserId,
            InstalledAt = time.GetUtcNow(),
        };
        extensions.Add(install);
        audit.Record(actorUserId, "extension.imported", AuditTargets.Extension, install.Id, null, Snapshot(install));

        logger.LogInformation("Extension {PackageId} imported as {InstallId}", install.PackageId, install.Id);
        return new(await Describe(install, cancellationToken), null);
    }

    public async Task<List<ExtensionState>> ListInstalled(CancellationToken cancellationToken)
    {
        List<ExtensionState> states = [];

        foreach (var install in await extensions.ListInstalled(cancellationToken))
        {
            states.Add(await Describe(install, cancellationToken));
        }

        return states;
    }

    /// <summary>One installed extension as the screen shows it, or null when this space has none by that id.</summary>
    public async Task<ExtensionState?> Find(Guid installId, CancellationToken cancellationToken) =>
        await extensions.FindInstalled(installId, cancellationToken) is { } install ? await Describe(install, cancellationToken) : null;

    /// <summary>The themes the space's installed extensions offer its members, the newest extension's first.</summary>
    public async Task<List<SpaceTheme>> ListThemes(CancellationToken cancellationToken) =>
    [
        .. (await extensions.ListInstalled(cancellationToken)).SelectMany(install =>
            (ExtensionPackages.ReadKept(install.Package)?.Themes() ?? []).Select(theme => new SpaceTheme(theme.Id, theme.Name, install.Name, theme.File))),
    ];

    /// <summary>The tool server the package names, or null when it names none.</summary>
    public static ConnectionRecipe? RecipeOf(ExtensionPackage? package) => package?.Contents.OfType<ConnectionRecipe>().SingleOrDefault();

    /// <summary>The settings that start with a value, by key. A secret never has one.</summary>
    public static Dictionary<string, string> Defaults(ExtensionPackage package) =>
        package.Settings.Where(setting => setting.Default is not null).ToDictionary(setting => setting.Key, setting => setting.Default!);

    public static JsonObject Snapshot(ExtensionInstall install) => new()
    {
        ["package_id"] = install.PackageId,
        ["name"] = install.Name,
        ["content_hash"] = install.ContentHash,
    };

    #endregion

    #region Private Methods

    /// <summary>
    /// The extension with how far its tool server has come: whether it is connected, what its last listing said, and
    /// for each suggested tool whether the server lists it.
    /// </summary>
    private async Task<ExtensionState> Describe(ExtensionInstall install, CancellationToken cancellationToken)
    {
        var package = ExtensionPackages.ReadKept(install.Package);

        if (RecipeOf(package) is not { } recipe)
        {
            return new(install, package, null);
        }

        var connection = install.ToolConnectionId is { } id ? await toolConnections.FindConnection(id, cancellationToken) : null;
        var listed = connection is null ? [] : await toolConnections.ListTools(connection.Id, cancellationToken);
        var values = install.Settings.Count > 0 ? install.Settings : Defaults(package!);

        return new(install, package, recipe.ToPreview(values, policy.Deployment.AllowToolConnections).ToSummary() with
        {
            ConnectionId = connection?.Id,
            Status = connection is null ? null : Words.Of(connection.Status),
            Problem = connection?.LastError,
            Tools =
            [
                .. recipe.SuggestedTools.Select(suggested => new SuggestedToolState(
                    suggested.Name,
                    suggested.Why,
                    listed.FirstOrDefault(found => found.Name == suggested.Name && found.State is AvailabilityState.Available)?.Id)),
            ],
        });
    }

    #endregion
}
