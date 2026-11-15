namespace Drover.Engine.Tools;

/// <summary>
/// <c>delegate</c>: a bot hands a task to a bot directly beneath it and gets the answer back as the tool's result.
/// Like every built-in tool it is a catalog row, off until granted and narrowed by every grant above. Its calls do
/// not run through <see cref="BuiltinTool.Run"/>: the runtime exposes the child as a function and the governor
/// begins and executes the child run, which works with its own grants, history, and events.
/// </summary>
public static class DelegateTool
{
    #region Private Fields

    public const string Name = "delegate";

    public const string BotIdArgument = "bot_id";

    public const string TaskArgument = "task";

    private const string Description =
        "Hands a task to a bot directly beneath this one and returns its answer. The bot works on its own, with its own tools, and sees only the task, so say everything it needs.";

    #endregion

    #region Public Methods

    public static IServiceCollection AddDelegateTool(this IServiceCollection services) =>
        services.AddSingleton(_ => Delegate());

    public static BuiltinTool Delegate() =>
        new(
            Name,
            "Hand work to a bot",
            Description,
            JsonElement.Parse(
                $$"""
                {
                  "type": "object",
                  "properties": {
                    "{{BotIdArgument}}": { "type": "string", "format": "uuid", "description": "The id of a bot directly beneath this one, as this tool's description lists them." },
                    "{{TaskArgument}}": { "type": "string", "description": "The task, in full: the bot sees nothing else of this conversation." }
                  },
                  "required": ["{{BotIdArgument}}", "{{TaskArgument}}"]
                }
                """),
            ToolSensitivity.Write,
            (_, _) => throw new BuiltinToolException("A delegation is run by the runtime, not as a plain tool."));

    /// <summary>
    /// What the model is told about the tool: what it does, and the bots it may hand work to, each with what it is for.
    /// A bot that is paused is listed as paused, so the model is not surprised when a hand-off to it is refused.
    /// </summary>
    public static string DescribeFor(IReadOnlyList<Bot> children) =>
        children.Count == 0
            ? $"{Description} No bot is beneath this one yet, so there is nobody to hand work to."
            : $"{Description} The bots beneath this one:\n{string.Join("\n", children.Select(Describe))}";

    #endregion

    #region Private Methods

    private static string Describe(Bot bot)
    {
        var purpose = string.IsNullOrWhiteSpace(bot.DescriptionForBots) ? "no description" : bot.DescriptionForBots.Trim();
        var paused = bot.Status is BotStatus.Paused ? " (paused)" : string.Empty;
        return $"- {bot.Id}: {bot.Name}{paused}. {purpose}";
    }

    #endregion
}
