namespace Drover.Engine.Bots;

/// <summary>What creating a bot came to: the bot, or a sentence for the person who asked.</summary>
public sealed record BotChange(Bot? Bot, string? Problem);

/// <summary>
/// Bots of a space. Runs in the tenant scope the endpoint filter opened. The bot editor (grants, hierarchy, every
/// setting) arrives with its own step; this creates a bot at the top of the tree with what a chat needs.
/// </summary>
public sealed class BotService(
    BotRepository bots,
    ModelConnectionRepository connections,
    ToolRepository tools,
    ToolCatalog catalog,
    ILogger<BotService> logger)
{
    #region Private Fields

    private const int MaxNameLength = 200;

    private const int MaxHandleLength = 100;

    private const int MaxInstructionsLength = 32_000;

    private const int MaxModelLength = 200;

    #endregion

    #region Public Methods

    public Task<List<Bot>> ListBots(CancellationToken cancellationToken) => bots.ListBots(cancellationToken);

    public async Task<Bot?> FindBot(Guid id, CancellationToken cancellationToken) =>
        await bots.FindBot(id, cancellationToken) is { Status: not BotStatus.Removed } bot ? bot : null;

    /// <summary>
    /// Creates a bot at the top of the tree. It is granted the built-in tools that only read, as allow, so it can use
    /// them at once; every other tool stays off until a person grants it.
    /// </summary>
    public async Task<BotChange> Create(
        string name,
        string instructions,
        Guid modelConnectionId,
        string? model,
        Guid createdByUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
        {
            return new BotChange(null, $"Give the bot a name of up to {MaxNameLength} characters.");
        }

        if (instructions.Length > MaxInstructionsLength)
        {
            return new BotChange(null, $"Keep the instructions under {MaxInstructionsLength} characters.");
        }

        var connection = await connections.FindConnection(modelConnectionId, cancellationToken);

        if (connection is null)
        {
            return new BotChange(null, "Choose a model connection of this space.");
        }

        var chosen = string.IsNullOrWhiteSpace(model) ? connection.DefaultModel : model.Trim();

        if (string.IsNullOrWhiteSpace(chosen) || chosen.Length > MaxModelLength)
        {
            return new BotChange(null, "Choose a model for the bot.");
        }

        var bot = new Bot
        {
            Name = name.Trim(),
            Handle = await FindFreeHandle(name, cancellationToken),
            Instructions = instructions,
            ModelConnectionId = connection.Id,
            Model = chosen,
            CreatedByUserId = createdByUserId,
        };
        bots.AddRootBot(bot);
        await GrantReadOnlyBuiltins(bot, createdByUserId, cancellationToken);
        logger.LogInformation("Bot {BotId} created", bot.Id);
        return new BotChange(bot, null);
    }

    #endregion

    #region Private Methods

    private async Task GrantReadOnlyBuiltins(Bot bot, Guid grantedByUserId, CancellationToken cancellationToken)
    {
        foreach (var tool in await catalog.EnsureBuiltins(cancellationToken))
        {
            if (tool.Sensitivity is ToolSensitivity.Read)
            {
                tools.Add(new ToolGrant { BotId = bot.Id, ToolId = tool.Id, Effect = GrantEffect.Allow, GrantedByUserId = grantedByUserId });
            }
        }
    }

    private async Task<string> FindFreeHandle(string name, CancellationToken cancellationToken)
    {
        var slug = new string([.. name.Trim().ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-')])
            .Trim('-');
        var stem = slug.Length == 0 ? "bot" : slug[..Math.Min(slug.Length, MaxHandleLength - 8)];
        var handle = stem;

        for (var suffix = 2; await bots.HandleExists(handle, cancellationToken); suffix++)
        {
            handle = $"{stem}-{suffix}";
        }

        return handle;
    }

    #endregion
}
