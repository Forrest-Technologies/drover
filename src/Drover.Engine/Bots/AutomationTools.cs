namespace Drover.Engine.Bots;

/// <summary>Native routine and rule tools, using the same services as the bot editor.</summary>
public static class AutomationTools
{
    #region Private Fields

    private const string RoutineListName = "routine.list";
    private const string RoutineOwnName = "routine.manage_own";
    private const string RoutineDescendantName = "routine.manage_descendant";
    private const string RuleListName = "rule.list";
    private const string RuleOwnName = "rule.manage_own";
    private const string RuleDescendantAddName = "rule.add_for_descendant";
    private const string RuleDescendantChangeName = "rule.change_for_descendant";

    private const string BotId =
        """
        "bot_id": { "type": "string", "description": "The id of a bot beneath you, as returned by the list tool." }
        """;

    private const string ItemId =
        """
        "id": { "type": "string", "description": "The id returned by the list tool. Required to change or remove." }
        """;

    private const string Routine =
        """
        "routine": {
          "type": "object",
          "description": "The full routine to add or replace. Omit to remove.",
          "properties": {
            "cron": { "type": "string", "description": "Five-field cron, for example 0 6 * * * for 6 AM daily." },
            "time_zone": { "type": "string", "description": "An IANA time zone, for example America/New_York." },
            "prompt": { "type": "string", "description": "The full prompt sent to the bot each time." },
            "thread_mode": { "type": "string", "enum": ["new_thread", "same_thread"] },
            "enabled": { "type": "boolean" }
          },
          "required": ["cron", "time_zone", "prompt", "thread_mode", "enabled"]
        }
        """;

    private const string Rule =
        """
        "rule": {
          "type": "object",
          "description": "The full rule to add or replace. Omit to remove.",
          "properties": {
            "name": { "type": "string" },
            "enabled": { "type": "boolean" },
            "checkpoint": { "type": "string", "enum": ["before_tool", "after_tool", "before_reply", "message_arrives"] },
            "effect": { "type": "string", "enum": ["redact", "ask", "block", "stop"] },
            "conditions": {
              "type": "array",
              "description": "All conditions must match. Empty means always. Each object has kind and its inputs: tool_is (tools), tool_kind (tool_kinds), text_contains (entries, argument), text_matches (pattern, argument), text_holds (builtin, argument), not_in_list (entries, argument), time_is (from, to, weekdays, time_zone), run_exceeds (counter, limit), judged (statement, min_confidence).",
              "items": {
                "type": "object",
                "properties": { "kind": { "type": "string" } },
                "required": ["kind"]
              }
            }
          },
          "required": ["name", "enabled", "checkpoint", "effect", "conditions"]
        }
        """;

    private static readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    #endregion

    #region Properties

    public static IReadOnlyList<string> AllowedByDefault { get; } =
        [RoutineListName, RoutineOwnName, RuleListName, RuleOwnName, RuleDescendantAddName];

    public static IReadOnlyList<string> AskedByDefault { get; } = [RoutineDescendantName, RuleDescendantChangeName];

    #endregion

    #region Public Methods

    public static IServiceCollection AddAutomationTools(this IServiceCollection services)
    {
        services.AddScoped<BotAutomation>();
        services.AddSingleton(_ => ListRoutines());
        services.AddSingleton(_ => ManageRoutine(RoutineOwnName, descendant: false));
        services.AddSingleton(_ => ManageRoutine(RoutineDescendantName, descendant: true));
        services.AddSingleton(_ => ListRules());
        services.AddSingleton(_ => ManageRule(RuleOwnName, descendant: false, operations: ["add", "change", "remove"]));
        services.AddSingleton(_ => ManageRule(RuleDescendantAddName, descendant: true, operations: ["add"]));
        return services.AddSingleton(_ => ManageRule(RuleDescendantChangeName, descendant: true, operations: ["change", "remove"]));
    }

    #endregion

    #region Private Methods

    private static BuiltinTool ListRoutines() =>
        new(
            RoutineListName,
            "List routines",
            "Lists scheduled prompts for the person you are working for. Leave bot_id out for your own routines. The result also lists bots beneath the selected bot.",
            Schema(BotId),
            ToolSensitivity.Read,
            (call, cancellationToken) => Use(call, false,
                (service, target) => service.ListRoutines(target, call.Tenant.UserId, cancellationToken), cancellationToken));

    private static BuiltinTool ManageRoutine(string name, bool descendant) =>
        new(
            name,
            descendant ? "Manage a descendant's routines" : "Manage your routines",
            descendant
                ? "Adds, replaces, or removes a scheduled prompt for a bot beneath you. The routine runs for the person you are working for. Use routine_list first to find the bot and routine."
                : "Adds, replaces, or removes one of your own scheduled prompts for the person you are working for. Use routine_list to find existing routines before changing them.",
            MutationSchema(descendant, ["add", "change", "remove"], Routine),
            ToolSensitivity.Write,
            (call, cancellationToken) => Use(call, true, (service, target) =>
            {
                var operation = Operation(call.Arguments, ["add", "change", "remove"]);
                var id = operation == "add" ? Guid.Empty : RequiredId(call.Arguments, "id");
                var edit = operation == "remove" ? null : ReadRoutine(call);
                return service.ManageRoutine(target.Id, operation, id, edit, Actor(call), cancellationToken);
            }, cancellationToken, descendant));

    private static BuiltinTool ListRules() =>
        new(
            RuleListName,
            "List rules",
            "Lists rules a bot follows, including inherited rules, and says which you may change. Leave bot_id out to list your own. The result lists the bots beneath the selected bot.",
            Schema(BotId),
            ToolSensitivity.Read,
            (call, cancellationToken) => Use(call, false,
                (service, target) => service.ListRules(target, Actor(call), cancellationToken), cancellationToken));

    private static BuiltinTool ManageRule(string name, bool descendant, string[] operations) =>
        new(
            name,
            name switch
            {
                RuleOwnName => "Manage your rules",
                RuleDescendantAddName => "Add a rule for a descendant",
                _ => "Change a descendant's rules",
            },
            name switch
            {
                RuleOwnName => "Adds, replaces, or removes rules you write for yourself. Rules also apply to your descendants. Rules can only restrict behavior.",
                RuleDescendantAddName => "Adds a rule that restricts a bot beneath you and its descendants. Use rule_list to find the bot and its current rules.",
                _ => "Replaces or removes bot-authored rules on a bot beneath you. Use rule_list to find a rule you may change.",
            },
            MutationSchema(descendant, operations, Rule),
            ToolSensitivity.Write,
            (call, cancellationToken) => Use(call, true, (service, target) =>
            {
                var operation = Operation(call.Arguments, operations);
                var id = operation == "add" ? Guid.Empty : RequiredId(call.Arguments, "id");
                var body = operation == "remove" ? null : ReadRule(call);
                return service.ManageRule(target.Id, operation, id, body, Actor(call), cancellationToken);
            }, cancellationToken, descendant));

    private static Task<JsonElement> Use(
        BuiltinToolCall call,
        bool writes,
        Func<BotAutomation, Bot, Task<object>> action,
        CancellationToken cancellationToken,
        bool? descendant = null)
    {
        if (writes && call.IsTainted() && !call.Approved)
        {
            throw new BuiltinToolException("This run read an untrusted source. Make this change in a fresh conversation or use a grant that asks for approval.");
        }

        return call.Tenant.Transact(async services =>
        {
            var automation = services.GetRequiredService<BotAutomation>();
            var target = await automation.Target(call.BotId, TargetId(call, descendant), writes, cancellationToken);
            return JsonSerializer.SerializeToElement(await action(automation, target), json);
        }, cancellationToken);
    }

    private static Guid TargetId(BuiltinToolCall call, bool? descendant)
    {
        var specified = call.Arguments.ValueKind is JsonValueKind.Object && call.Arguments.TryGetProperty("bot_id", out _);

        if (descendant is false && specified)
        {
            throw new BuiltinToolException("Use the descendant tool when naming a bot.");
        }

        if (descendant is not true && !specified)
        {
            return call.BotId;
        }

        var target = RequiredId(call.Arguments, "bot_id");
        return descendant is true && target == call.BotId
            ? throw new BuiltinToolException("Use the tool for your own routines or rules when changing yourself.")
            : target;
    }

    private static AuditActor Actor(BuiltinToolCall call) => AuditActor.BotFor(call.BotId, call.Tenant.UserId);

    private static string Operation(JsonElement arguments, string[] allowed)
    {
        var operation = Text(arguments, "operation");
        return allowed.Contains(operation, StringComparer.Ordinal)
            ? operation
            : throw new BuiltinToolException($"Choose an operation: {string.Join(", ", allowed)}.");
    }

    private static ScheduleEdit ReadRoutine(BuiltinToolCall call)
    {
        var routine = Object(call.Arguments, "routine");
        return new ScheduleEdit(
            Text(routine, "cron"),
            Text(routine, "time_zone"),
            call.Redact(Text(routine, "prompt")),
            Words.Read<ScheduleThreadMode>(Text(routine, "thread_mode")),
            Enabled(routine));
    }

    private static BotRuleBody ReadRule(BuiltinToolCall call)
    {
        var rule = Object(call.Arguments, "rule");

        if (!rule.TryGetProperty("conditions", out var conditions) || conditions.ValueKind is not JsonValueKind.Array
            || conditions.EnumerateArray().Any(condition => condition.ValueKind is not JsonValueKind.Object))
        {
            throw new BuiltinToolException("Give the rule a conditions array. Use an empty array only for an unconditional rule.");
        }

        IReadOnlyList<RuleConditionBody> read;
        try
        {
            read = call.RedactJson(conditions).Deserialize<List<RuleConditionBody>>(json)!;
        }
        catch (JsonException)
        {
            throw new BuiltinToolException("A rule condition has an invalid value or type.");
        }

        return new BotRuleBody(call.Redact(Text(rule, "name")), Enabled(rule), Text(rule, "checkpoint"),
            Text(rule, "effect"), read);
    }

    private static bool Enabled(JsonElement value) =>
        value.TryGetProperty("enabled", out var enabled) && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? enabled.GetBoolean()
            : throw new BuiltinToolException("The enabled argument must be true or false.");

    private static string Text(JsonElement value, string name) =>
        value.ValueKind is JsonValueKind.Object && value.TryGetProperty(name, out var text) && text.ValueKind is JsonValueKind.String && text.GetString() is { } read
            ? read
            : throw new BuiltinToolException($"The argument {name} is required and must be text.");

    private static Guid RequiredId(JsonElement arguments, string name) =>
        Guid.TryParse(Text(arguments, name), out var id)
            ? id
            : throw new BuiltinToolException($"The argument {name} must be an id from the list tool.");

    private static JsonElement Object(JsonElement arguments, string name) =>
        arguments.ValueKind is JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Object
            ? value
            : throw new BuiltinToolException($"The argument {name} is required and must be an object.");

    private static JsonElement MutationSchema(bool descendant, string[] operations, string body)
    {
        var values = JsonSerializer.Serialize(operations);
        var target = descendant ? $"{BotId}," : string.Empty;
        var operation = $$"""
            "operation": { "type": "string", "enum": {{values}} }
            """;
        return Schema($"{target}{operation},{ItemId},{body}", descendant ? ["bot_id", "operation"] : ["operation"]);
    }

    private static JsonElement Schema(string properties, params string[] required) =>
        JsonElement.Parse($$"""
            { "type": "object", "properties": { {{properties}} }, "required": {{JsonSerializer.Serialize(required)}} }
            """);

    #endregion
}
