namespace Drover.Engine.Extensions;

/// <summary>The result of reading or importing an extension: what came of it, or the sentence that says why not.</summary>
public sealed record ExtensionChange<T>(T? Value, string? Problem)
    where T : class;

/// <summary>The model an imported bot thinks with, and the connection it is on.</summary>
public sealed record ModelChoice(ModelConnection Connection, string Model);

/// <summary>
/// Extensions of the current space: reading a file for a person to review, importing it, and listing what is
/// installed. An extension never grants anything and its bot starts paused, so importing one changes what exists and
/// not what anything may do. The caller, an endpoint, owns the tenant scope.
/// </summary>
public sealed class ExtensionService(
    ExtensionRepository extensions,
    ModelConnectionRepository connections,
    BotService bots,
    AuditLog audit,
    TimeProvider time,
    ILogger<ExtensionService> logger)
{
    #region Private Fields

    /// <summary>What an import answers with when the space has no model for the bot. The screen says the same and links to it.</summary>
    public const string NoModel = "A bot needs a model to think with, and this space has none it can use yet. Connect a model, then import this.";

    #endregion

    #region Public Methods

    /// <summary>What the file would add, for a person to read first. Nothing is stored and nothing is contacted.</summary>
    public async Task<ExtensionChange<ExtensionPreview>> Preview(string? text, CancellationToken cancellationToken)
    {
        var reading = ExtensionPackages.Read(text);

        if (reading.Package is not { } package)
        {
            return new(null, reading.Problem);
        }

        var bot = package.Contents.OfType<BotTemplate>().SingleOrDefault();

        return new(
            new ExtensionPreview(package.Id, package.Name.Trim(), package.Description)
            {
                Bot = bot?.ToPreview(await ChooseModel(bot, cancellationToken)),
                AlreadyInstalled = await extensions.IsInstalled(package.Id, cancellationToken),
            },
            null);
    }

    /// <summary>
    /// Adds what the file holds. Its bot is made the way a person makes one, on the model the preview named, and is
    /// paused until they turn it on: its instructions came from somebody else.
    /// </summary>
    public async Task<ExtensionChange<InstalledExtension>> Import(string? text, Guid actorUserId, CancellationToken cancellationToken)
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

        Bot? bot = null;

        if (package.Contents.OfType<BotTemplate>().SingleOrDefault() is { } template)
        {
            if (await ChooseModel(template, cancellationToken) is not { } model)
            {
                return new(null, NoModel);
            }

            var made = await bots.Create(
                new NewBot(template.Name, template.Description, template.Instructions, model.Connection.Id, model.Model, null) { Paused = true },
                AuditActor.Person(actorUserId),
                cancellationToken);

            if (made.Bot is null)
            {
                return new(null, made.Problem);
            }

            bot = made.Bot;
        }

        var install = new ExtensionInstall
        {
            PackageId = package.Id,
            Name = package.Name.Trim(),
            Description = package.Description,
            ContentHash = reading.ContentHash,
            Package = JsonElement.Parse(text!),
            BotId = bot?.Id,
            InstalledByUserId = actorUserId,
            InstalledAt = time.GetUtcNow(),
        };
        extensions.Add(install);
        audit.Record(actorUserId, "extension.imported", AuditTargets.Extension, install.Id, null, Snapshot(install));

        logger.LogInformation("Extension {PackageId} imported as {InstallId}", install.PackageId, install.Id);
        return new(new InstalledExtension(install, bot?.Name, bot?.Status), null);
    }

    public Task<List<InstalledExtension>> ListInstalled(CancellationToken cancellationToken) =>
        extensions.ListInstalled(cancellationToken);

    #endregion

    #region Private Methods

    /// <summary>
    /// The model the bot would think with, or null when the space has none it could use. The connection is the one
    /// that last answered. The model is the file's suggestion when that connection lists it, then the connection's
    /// own default, then the first model it lists: a suggestion is advice, and never names a model the space lacks.
    /// </summary>
    private async Task<ModelChoice?> ChooseModel(BotTemplate bot, CancellationToken cancellationToken)
    {
        var connection = (await connections.ListConnections(cancellationToken))
            .Where(candidate => candidate.Status is not ConnectionStatus.Disabled)
            .OrderByDescending(candidate => candidate.Status is ConnectionStatus.Ready)
            .ThenByDescending(candidate => candidate.LastCheckedAt)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();

        if (connection is null)
        {
            return null;
        }

        var listed = (await connections.ListCatalog(connection.Id, cancellationToken))
            .Where(entry => entry.State is AvailabilityState.Available)
            .Select(entry => entry.Model)
            .ToList();
        var model = listed.FirstOrDefault(candidate => string.Equals(candidate, bot.SuggestedModel, StringComparison.Ordinal))
            ?? connection.DefaultModel
            ?? listed.FirstOrDefault();

        return string.IsNullOrWhiteSpace(model) ? null : new ModelChoice(connection, model);
    }

    private static JsonObject Snapshot(ExtensionInstall install) => new()
    {
        ["package_id"] = install.PackageId,
        ["name"] = install.Name,
        ["content_hash"] = install.ContentHash,
        ["bot_id"] = install.BotId,
    };

    #endregion
}
