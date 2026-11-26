namespace Drover.Engine.Bots;

/// <summary>The result of reading or importing a bot file: what came of it, or the sentence that says why not.</summary>
public sealed record BotFileChange<T>(T? Value, string? Problem)
    where T : class;

/// <summary>The model an imported bot thinks with, and the connection it is on.</summary>
public sealed record ModelChoice(ModelConnection Connection, string Model);

/// <summary>
/// Bots as files: writing one down so it can be shared, reading a file for a person to review, and importing it. A
/// file holds a bot's words and nothing it may do, and an imported bot starts paused, so importing one changes what
/// exists and not what anything is allowed. The caller, an endpoint, owns the tenant scope.
/// </summary>
public sealed class BotFileService(BotService bots, ModelConnectionRepository connections, ILogger<BotFileService> logger)
{
    #region Private Fields

    /// <summary>What an import answers with when the space has no model for the bot. The screen says the same and links to it.</summary>
    public const string NoModel = "A bot needs a model to think with, and this space has none it can use yet. Connect a model, then import this.";

    #endregion

    #region Public Methods

    /// <summary>
    /// The bot written down as a file: its name, what it is for, its instructions, and its model as a suggestion.
    /// Nothing else of the bot is ever put in a file: not its grants, sandbox, limits, schedules, rules, or notes.
    /// Null when the bot is not in this space.
    /// </summary>
    public async Task<BotFile?> Export(Guid botId, CancellationToken cancellationToken) =>
        await bots.FindBot(botId, cancellationToken) is { } bot
            ? new BotFile(BotFile.CurrentVersion, bot.Name, bot.Instructions) { Description = bot.DescriptionForBots, SuggestedModel = bot.Model }
            : null;

    /// <summary>The bot the file would add, for a person to read first. Nothing is stored and nothing is contacted.</summary>
    public async Task<BotFileChange<BotImportPreview>> Preview(string? text, CancellationToken cancellationToken)
    {
        var reading = BotFileReader.Read(text);

        if (reading.File is not { } file)
        {
            return new(null, reading.Problem);
        }

        var model = await ChooseModel(file, cancellationToken);
        var (instructions, hidden) = BotFileReader.ShowHidden(file.Instructions);
        return new(
            new BotImportPreview(file.Name.Trim(), file.Description, instructions, hidden) { Model = model?.Model, ModelConnection = model?.Connection.Name },
            null);
    }

    /// <summary>
    /// Adds the bot the file holds. It is made the way a person makes one, on the model the preview named, and is
    /// paused until they turn it on: its instructions came from somebody else.
    /// </summary>
    public async Task<BotFileChange<Bot>> Import(string? text, Guid actorUserId, CancellationToken cancellationToken)
    {
        var reading = BotFileReader.Read(text);

        if (reading.File is not { } file)
        {
            return new(null, reading.Problem);
        }

        if (await ChooseModel(file, cancellationToken) is not { } model)
        {
            return new(null, NoModel);
        }

        var made = await bots.Create(
            new NewBot(file.Name, file.Description, file.Instructions, model.Connection.Id, model.Model, null) { Paused = true },
            AuditActor.Person(actorUserId),
            cancellationToken);

        if (made.Bot is { } bot)
        {
            logger.LogInformation("Bot {BotId} imported from a file, paused", bot.Id);
        }

        return new(made.Bot, made.Problem);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// The model the bot would think with, or null when the space has none it could use. The connection is the one
    /// that last answered. The model is the file's suggestion when that connection lists it, then the connection's
    /// own default, then the first model it lists: a suggestion is advice, and never names a model the space lacks.
    /// </summary>
    private async Task<ModelChoice?> ChooseModel(BotFile bot, CancellationToken cancellationToken)
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

    #endregion
}
