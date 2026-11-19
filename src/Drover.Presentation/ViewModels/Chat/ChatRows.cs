namespace Drover.Presentation.ViewModels.Chat;

/// <summary>How one message of a conversation is laid out.</summary>
public enum ChatEntryShape
{
    /// <summary>A person's words and files, on their side.</summary>
    Person,

    /// <summary>Tool calls a bot made without words, under its words and in line with them.</summary>
    Tools,

    /// <summary>A bot's words inside work handed to it: no head beside each message, the card has it.</summary>
    Compact,

    /// <summary>A bot's words, spoken from its head on a plate beside them.</summary>
    Bot,
}

/// <summary>Which card stands for a tool call in a conversation.</summary>
public enum ToolCardShape
{
    /// <summary>The call as a plain tool card.</summary>
    Plain,

    /// <summary>Work handed to another bot, with that bot's own messages inside.</summary>
    Delegation,

    /// <summary>A request for a person at the bot's screen.</summary>
    Takeover,
}

/// <summary>
/// What the rows of the workspace say and how they are shaped, worked out apart from any drawing: a bot's row in
/// the crew, a message, a tool call's card, the tray of attached files. Both the screen and its tests read these.
/// </summary>
public static class ChatRows
{
    #region Crew

    /// <summary>
    /// A bot's row is read out as one sentence: the name, what the bot is doing, that it is paused, and what it has
    /// for this person. What is written inside it says the same to the eye.
    /// </summary>
    public static string CrewRowName(BotSummary bot, IReadOnlySet<Guid> working, IReadOnlyDictionary<Guid, Attention> forYou)
    {
        var spoken = working.Contains(bot.Id) ? $"{bot.Name}, thinking" : bot.Name;
        return AttentionWords.Announce(bot.Status is "paused" ? $"{spoken}, paused" : spoken, forYou.GetValueOrDefault(bot.Id));
    }

    public static bool IsPaused(BotSummary bot) => bot.Status is "paused";

    /// <summary>The crew's bots in the order their rows are read, each with how deep under the lead it stands.</summary>
    public static IReadOnlyList<(BotSummary Bot, int Depth)> CrewRows(IReadOnlyList<CrewNode> crew)
    {
        List<(BotSummary, int)> rows = [];
        Add(crew, 0);
        return rows;

        void Add(IReadOnlyList<CrewNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                rows.Add((node.Bot, depth));
                Add(node.Children, depth + 1);
            }
        }
    }

    #endregion

    #region Messages

    /// <summary>A bot that reasoned without words yet still speaks from its head, so its reasoning has a place to arrive.</summary>
    public static ChatEntryShape Shape(ChatMessage message, bool compact) => message switch
    {
        { FromPerson: true } => ChatEntryShape.Person,
        { Text.Length: 0, Reasoning: null } => ChatEntryShape.Tools,
        _ when compact => ChatEntryShape.Compact,
        _ => ChatEntryShape.Bot,
    };

    /// <summary>What a message is, read out before its words: who said it, or that it is what a bot did.</summary>
    public static string EntryName(ChatMessage message, string botName, string? asker) => message switch
    {
        { FromPerson: true } => asker is null ? message.SentBy ?? "You said" : $"{asker} asked",
        { Text.Length: 0, Reasoning: null } => $"What {botName} did",
        { Text.Length: 0 } => $"What {botName} thought",
        _ => $"{botName} said",
    };

    /// <summary>What a reply used is offered once, at the last words of a finished run, where there is a way to read it.</summary>
    public static bool OffersUsage(ChatMessage message, bool canRead) => canRead && message is { EndsReply: true, RunId: not null };

    /// <summary>Said under a message sent while the bot works, until the bot has read it.</summary>
    public static string Pending(string botName) => $"Sent. {botName} reads it at its next step.";

    /// <summary>What a bot's reply says it is doing while it arrives: thinking until its words begin, then writing.</summary>
    public static string Arriving(ChatMessage message) => message.Thinking ? "Thinking" : "Writing";

    /// <summary>What a source is read out as: its title and the site it opens, outside the app.</summary>
    public static string SourceName(SourceLink source) =>
        source.Title == source.Site ? $"{source.Site}, opens outside the app" : $"{source.Title}, {source.Site}, opens outside the app";

    /// <summary>
    /// A waiting call inside handed work is asked for by the bot that made it. A request for a person at the bot's
    /// screen has a card of its own, where the conversation says what such a request needs.
    /// </summary>
    public static ToolCardShape Shape(ToolCard tool, ScreenTakeovers? takeovers) => (tool.Delegation, tool.Takeover, takeovers) switch
    {
        ({ }, _, _) => ToolCardShape.Delegation,
        (_, { }, { }) => ToolCardShape.Takeover,
        _ => ToolCardShape.Plain,
    };

    /// <summary>
    /// A bot beneath works on its own screen, which opens from its sandbox page. A person who does not use screens, or
    /// whose access is not known yet, is told so as for the bot's own request.
    /// </summary>
    public static ScreenTakeovers? ForBotBeneath(ScreenTakeovers? takeovers, DelegationItem work) => takeovers switch
    {
        null => null,
        { Access: ScreenAccess.Usable or ScreenAccess.Missing } =>
            takeovers with { HeldCallId = null, Access = ScreenAccess.Elsewhere, SetupHref = Addresses.BotSandbox(work.ChildId) },
        _ => takeovers with { HeldCallId = null },
    };

    #endregion

    #region Attached Files

    /// <summary>A file that cannot go: it was not uploaded, or the server refused it.</summary>
    public static bool HasProblem(AttachmentItem item) =>
        item.Phase is UploadPhase.Failed || item.Refusal is not FileRefusal.None;

    /// <summary>What the tray of attached files says aloud. Progress is announced when a file finishes or fails, not percent by percent.</summary>
    public static string Announced(IReadOnlyList<AttachmentItem> items)
    {
        var waiting = items.Count(item => item.Phase is UploadPhase.Uploading);
        var stopped = items.Count(HasProblem);
        return (waiting, stopped) switch
        {
            (0, 0) => $"{Words.Count(items.Count, "file")} ready to send.",
            (0, _) => $"{Words.Count(stopped, "file")} cannot be sent.",
            _ => $"Uploading {Words.Count(waiting, "file")}.",
        };
    }

    #endregion
}
