namespace Drover.Engine.Bots;

/// <summary>
/// The four crew tools: a bot lists, makes, changes, and removes the bots beneath it. They are on every server, so
/// each is a catalog row, off until granted and narrowed by every grant above like any other tool, and every call
/// passes the tool gate and its approval. What a call may touch is decided by <see cref="CrewService"/> from the
/// run's own bot, inside one short transaction as the run's person; the ids the model names are only what it asks for.
/// </summary>
public static class CrewTools
{
    #region Private Fields

    private const string BotIdProperty =
        """
        "bot_id": { "type": "string", "format": "uuid", "description": "The id of a bot beneath this one, as crew_list gives it." }
        """;

    private static readonly JsonSerializerOptions resultJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    #endregion

    #region Properties

    public static IReadOnlyList<string> Names { get; } = ["crew.list", "crew.add", "crew.change", "crew.remove"];

    #endregion

    #region Public Methods

    public static IServiceCollection AddCrewTools(this IServiceCollection services)
    {
        services.AddScoped<CrewService>();
        services.AddSingleton(_ => ListCrew());
        services.AddSingleton(_ => AddToCrew());
        services.AddSingleton(_ => ChangeCrew());
        return services.AddSingleton(_ => RemoveFromCrew());
    }

    public static bool IsCrewTool(string name) => Names.Contains(name, StringComparer.Ordinal);

    public static BuiltinTool ListCrew() =>
        new(
            "crew.list",
            "List the crew",
            "Lists the bots beneath this bot, at every depth, nearest first: each one's id, name, handle, what it is for, status, and the id of the bot it sits beneath.",
            JsonElement.Parse("""{ "type": "object", "properties": {} }"""),
            ToolSensitivity.Read,
            async (call, cancellationToken) =>
            {
                var crew = await call.Tenant.Transact(
                    services => services.GetRequiredService<CrewService>().List(call.BotId, cancellationToken),
                    cancellationToken);
                return Result(new { bots = crew.Select(Describe) });
            });

    public static BuiltinTool AddToCrew() =>
        new(
            "crew.add",
            "Make a bot",
            "Makes a new bot beneath this bot, or beneath a bot already beneath it. The new bot talks to the same model as this bot and starts with no more tools than the bot it sits beneath. A person is always asked to approve each new bot before it is made. Returns the new bot.",
            Schema(
                """
                "name": { "type": "string", "description": "The new bot's name." },
                "description": { "type": "string", "description": "What the new bot is for, in a sentence or two other bots can read." },
                "instructions": { "type": "string", "description": "How the new bot works: the instructions it follows." },
                "parent_id": { "type": "string", "format": "uuid", "description": "The id of a bot beneath this one to put the new bot beneath. Leave it out to put it directly beneath this bot." }
                """,
                "name", "description", "instructions"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var arguments = call.Arguments;
                var addition = new CrewAddition(
                    Required(arguments, "name"),
                    Optional(arguments, "description") ?? string.Empty,
                    Optional(arguments, "instructions") ?? string.Empty,
                    OptionalId(arguments, "parent_id"));
                var made = await call.Tenant.Transact(
                    services => services.GetRequiredService<CrewService>().Add(call.BotId, addition, cancellationToken),
                    cancellationToken);
                return Result(Describe(made));
            })
        {
            AlwaysAsks = true,
            DescribeForApproval = (call, cancellationToken) => call.Tenant.Transact(
                services => services.GetRequiredService<CrewService>().DescribeAddition(call.BotId, OptionalId(call.Arguments, "parent_id"), cancellationToken),
                cancellationToken),
        };

    public static BuiltinTool ChangeCrew() =>
        new(
            "crew.change",
            "Change a bot",
            "Changes a bot beneath this bot: its name, what it is for, its instructions, or whether it is paused. A part left out stays as it is.",
            Schema(
                $$"""
                {{BotIdProperty.Trim()}},
                "name": { "type": "string", "description": "The bot's new name." },
                "description": { "type": "string", "description": "What the bot is for, in a sentence or two other bots can read." },
                "instructions": { "type": "string", "description": "The instructions the bot follows, in full." },
                "status": { "type": "string", "enum": ["active", "paused"], "description": "paused stops the bot from working until it is made active again." }
                """,
                "bot_id"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var arguments = call.Arguments;
                var targetId = RequiredId(arguments, "bot_id");
                var change = new CrewChange(
                    Optional(arguments, "name"),
                    Optional(arguments, "description", allowEmpty: true),
                    Optional(arguments, "instructions", allowEmpty: true),
                    Status(arguments));
                var changed = await call.Tenant.Transact(
                    services => services.GetRequiredService<CrewService>().Change(call.BotId, targetId, change, cancellationToken),
                    cancellationToken);
                return Result(Describe(changed));
            });

    public static BuiltinTool RemoveFromCrew() =>
        new(
            "crew.remove",
            "Remove a bot",
            "Removes a bot beneath this bot. It stops working; its conversations are kept, and a person can still read them. If it has bots beneath it, first ask the person whether those are removed with it or kept, and pass their answer as beneath; never choose for them.",
            Schema(
                $$"""
                {{BotIdProperty.Trim()}},
                "beneath": { "type": "string", "enum": ["remove", "keep"], "description": "Only when the bot has bots beneath it, and only as the person chose: remove takes them all with it; keep moves them up beneath the bot it sat beneath." }
                """,
                "bot_id"),
            ToolSensitivity.Write,
            async (call, cancellationToken) =>
            {
                var targetId = RequiredId(call.Arguments, "bot_id");
                var beneath = Beneath(call.Arguments);
                var removal = await call.Tenant.Transact(
                    services => services.GetRequiredService<CrewService>().Remove(call.BotId, targetId, beneath, cancellationToken),
                    cancellationToken);
                var moved = removal.Beneath.Count > 0 ? removal.Beneath : null;
                return Result(new
                {
                    id = removal.Bot!.Id,
                    name = removal.Bot.Name,
                    status = Words.Of(removal.Bot.Status),
                    beneath = moved is null || beneath is not { } chosen ? null : Words.Of(chosen),
                    bots_beneath = moved,
                });
            });

    #endregion

    #region Arguments

    private static object Describe(Bot bot) => new
    {
        id = bot.Id,
        name = bot.Name,
        handle = bot.Handle,
        description = bot.DescriptionForBots,
        status = Words.Of(bot.Status),
        parent_id = bot.ParentBotId,
    };

    /// <summary>A result as the model reads it. A field with nothing to say is left out.</summary>
    private static JsonElement Result(object value) => JsonSerializer.SerializeToElement(value, resultJson);

    private static JsonElement Schema(string properties, params string[] required) =>
        JsonElement.Parse(
            $$"""
            {
              "type": "object",
              "properties": {
                {{properties.Trim()}}
              },
              "required": [{{string.Join(", ", required.Select(name => $"\"{name}\""))}}]
            }
            """);

    private static string Required(JsonElement arguments, string name) =>
        Optional(arguments, name) ?? throw new BuiltinToolException($"The argument {name} is required and must be text.");

    private static string? Optional(JsonElement arguments, string name, bool allowEmpty = false) =>
        arguments.ValueKind is JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
        && value.GetString() is { } text
        && (allowEmpty || !string.IsNullOrWhiteSpace(text))
            ? text
            : null;

    private static Guid RequiredId(JsonElement arguments, string name) =>
        OptionalId(arguments, name) ?? throw new BuiltinToolException($"The argument {name} is required and must be a bot's id, as crew_list gives it.");

    private static Guid? OptionalId(JsonElement arguments, string name) =>
        Optional(arguments, name) is not { } text
            ? null
            : Guid.TryParse(text.Trim(), out var id)
                ? id
                : throw new BuiltinToolException($"The argument {name} must be a bot's id, as crew_list gives it.");

    private static BotsBeneath? Beneath(JsonElement arguments) =>
        Optional(arguments, "beneath") is not { } word
            ? null
            : Words.Read<BotsBeneath>(word) ?? throw new BuiltinToolException("The argument beneath is remove or keep, as the person chose.");

    private static BotStatus? Status(JsonElement arguments) =>
        Optional(arguments, "status") switch
        {
            null => null,
            "active" => BotStatus.Active,
            "paused" => BotStatus.Paused,
            _ => throw new BuiltinToolException("The argument status is active or paused."),
        };

    #endregion
}
