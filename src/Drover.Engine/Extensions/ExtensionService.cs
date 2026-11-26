namespace Drover.Engine.Extensions;

/// <summary>The result of reading or importing an extension: what came of it, or the sentence that says why not.</summary>
public sealed record ExtensionChange<T>(T? Value, string? Problem)
    where T : class;

/// <summary>An installed extension as the screen shows it: the row, its bot, and how far its tool server's setup has come.</summary>
public sealed record ExtensionState(InstalledExtension Installed, ExtensionConnectionSummary? Connection);

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
    ToolConnectionRepository toolConnections,
    ToolRepository tools,
    PolicyService policy,
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
                Connection = package.Contents.OfType<ConnectionRecipe>().SingleOrDefault()?.ToPreview(policy.Deployment.AllowToolConnections),
                AlreadyInstalled = await extensions.IsInstalled(package.Id, cancellationToken),
            },
            null);
    }

    /// <summary>
    /// Adds what the file holds. Its bot is made the way a person makes one, on the model the preview named, and is
    /// paused until they turn it on: its instructions came from somebody else.
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
        return new(new ExtensionState(new InstalledExtension(install, bot?.Name, bot?.Status), await DescribeConnection(install, cancellationToken)), null);
    }

    /// <summary>
    /// The bot written down as an extension, for the editor to open. A bot made by hand is written as a bot-only
    /// package. A bot an extension added keeps that file's id, name and description, and its reviewed connection
    /// recipe: the server address, auth method, header name and suggested tools, never a credential or a grant.
    /// Nothing else of the bot has a field to be in, so its sandbox, limits, schedules and notes cannot leave.
    /// Null when the bot is not in this space.
    /// </summary>
    public async Task<ExtensionPackage?> Export(Guid botId, CancellationToken cancellationToken)
    {
        if (await bots.FindBot(botId, cancellationToken) is not { } bot)
        {
            return null;
        }

        var installed = await extensions.FindForBot(botId, cancellationToken);
        var recipe = installed is null ? null : RecipeOf(installed.Install);
        List<ExtensionContent> contents =
        [
            new BotTemplate(bot.Name, bot.Instructions) { Description = bot.DescriptionForBots, SuggestedModel = bot.Model },
        ];

        if (recipe is not null)
        {
            contents.Add(recipe);
        }

        return new ExtensionPackage(
            ExtensionPackage.CurrentVersion,
            installed?.PackageId ?? ExtensionFile.IdFrom(bot.Handle),
            installed?.Name ?? bot.Name)
        {
            Description = installed?.Description ?? string.Empty,
            Contents = contents,
        };
    }

    /// <summary>The file as its package, read as strictly as an import reads it, or the sentence that says why not.</summary>
    public static ExtensionChange<ExtensionPackage> Read(string? text)
    {
        var reading = ExtensionPackages.Read(text);
        return new(reading.Package, reading.Problem);
    }

    public async Task<List<ExtensionState>> ListInstalled(CancellationToken cancellationToken)
    {
        List<ExtensionState> states = [];

        foreach (var installed in await extensions.ListInstalled(cancellationToken))
        {
            states.Add(new(installed, await DescribeConnection(installed.Install, cancellationToken)));
        }

        return states;
    }

    /// <summary>One installed extension as the screen shows it, or null when this space has none by that id.</summary>
    public async Task<ExtensionState?> Find(Guid installId, CancellationToken cancellationToken) =>
        await extensions.FindInstalled(installId, cancellationToken) is { } installed
            ? new(installed, await DescribeConnection(installed.Install, cancellationToken))
            : null;

    /// <summary>The tool server the installed file names, or null when it names none.</summary>
    public static ConnectionRecipe? RecipeOf(ExtensionInstall install) =>
        ExtensionPackages.Read(install.Package.GetRawText()).Package?.Contents.OfType<ConnectionRecipe>().SingleOrDefault();

    #endregion

    #region Private Methods

    /// <summary>
    /// How far the extension's tool server has come: whether it is connected, what its last listing said, and for
    /// each suggested tool whether the server lists it and what the extension's bot holds for it.
    /// </summary>
    private async Task<ExtensionConnectionSummary?> DescribeConnection(ExtensionInstall install, CancellationToken cancellationToken)
    {
        if (RecipeOf(install) is not { } recipe)
        {
            return null;
        }

        var connection = install.ToolConnectionId is { } id ? await toolConnections.FindConnection(id, cancellationToken) : null;
        var listed = connection is null ? [] : await toolConnections.ListTools(connection.Id, cancellationToken);
        var grants = install.BotId is { } botId
            ? (await tools.ListOwnGrants(botId, cancellationToken)).ToDictionary(grant => grant.ToolId, grant => grant.Effect)
            : [];

        return recipe.ToPreview(policy.Deployment.AllowToolConnections).ToSummary() with
        {
            ConnectionId = connection?.Id,
            Status = connection is null ? null : Words.Of(connection.Status),
            Problem = connection?.LastError,
            Tools =
            [
                .. recipe.SuggestedTools.Select(suggested =>
                {
                    var tool = listed.FirstOrDefault(found => found.Name == suggested.Name && found.State is AvailabilityState.Available);
                    GrantEffect? grant = tool is not null && grants.TryGetValue(tool.Id, out var held) ? held : null;
                    return new SuggestedToolState(suggested.Name, suggested.Why, tool?.Id, GrantService.Word(grant));
                }),
            ],
        };
    }

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
