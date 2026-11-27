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

    /// <summary>A bot's words further on in its turn: under the turn's head, in line with its words, without a head of their own.</summary>
    Continued,
}

/// <summary>A part of a reply's footer, in the order the parts stand on its line.</summary>
public enum FooterPart
{
    Copy,
    ReadAloud,
    Usage,
    Regenerate,
    Versions,
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
        { ContinuesTurn: true } when !compact => ChatEntryShape.Continued,
        { Ledger: not null } when !compact => ChatEntryShape.Bot,
        { Text.Length: 0, Reasoning: null } => ChatEntryShape.Tools,
        _ when compact => ChatEntryShape.Compact,
        _ => ChatEntryShape.Bot,
    };

    /// <summary>What a message is, read out before its words: who said it, or a bot's activity or reasoning.</summary>
    public static string EntryName(ChatMessage message, string botName, string? asker) => message switch
    {
        { FromPerson: true } => asker is null ? message.SentBy ?? "You said" : $"{asker} asked",
        { UnusableToolRequest: true } => UnusableToolRequests.EntryName(botName),
        { Text.Length: 0, Ledger: not null } or { Text.Length: 0, Reasoning: null } => $"{botName}'s activity",
        { Text.Length: 0 } => $"{botName}'s reasoning",
        _ => $"{botName} said",
    };

    /// <summary>What a reply used is offered once, at the last words of a finished run, where there is a way to read it.</summary>
    public static bool OffersUsage(ChatMessage message, bool canRead) => canRead && message is { EndsReply: true, RunId: not null };

    /// <summary>
    /// A reply's words are offered to copy where its footer stands: on the last words of every finished reply, whoever
    /// it was written for, or a reply that has versions or may get another. Never while the words still arrive.
    /// </summary>
    public static bool OffersCopy(ChatMessage message) =>
        message is { FromPerson: false, Streaming: false, Text.Length: > 0 }
        && (message.FinishesReply || message.EndsReply || message.MayRegenerate || message.Versions is not null);

    /// <summary>
    /// A reply can be read aloud where it can be copied, once the server has stored it: the server reads a message by
    /// its id. Whether anything can speak is the footer's to ask.
    /// </summary>
    public static bool OffersReadAloud(ChatMessage message) =>
        OffersCopy(message) && !message.UnusableToolRequest && StoredId(message) is not null;

    /// <summary>The id the server keeps a message under, or null for one it has not stored yet.</summary>
    public static Guid? StoredId(ChatMessage message) => Guid.TryParse(message.Key, out var id) ? id : null;

    /// <summary>
    /// The parts of a reply's footer that it holds, in the order they stand on its line: copy, read aloud, the usage
    /// of the reply, another reply, then the switch between versions.
    /// </summary>
    public static IReadOnlyList<FooterPart> FooterParts(ChatMessage message, bool canRegenerate, bool canSwitch, bool canReadUsage)
    {
        List<FooterPart> parts = [];

        if (OffersCopy(message))
        {
            parts.Add(FooterPart.Copy);
        }

        if (OffersReadAloud(message))
        {
            parts.Add(FooterPart.ReadAloud);
        }

        if (OffersUsage(message, canReadUsage))
        {
            parts.Add(FooterPart.Usage);
        }

        if (!message.UnusableToolRequest && OffersRegenerate(message, canRegenerate))
        {
            parts.Add(FooterPart.Regenerate);
        }

        if (canSwitch && message.Versions is not null)
        {
            parts.Add(FooterPart.Versions);
        }

        return parts;
    }

    /// <summary>
    /// Another reply is offered where it is today: on a finished reply the server says may be written again, where the
    /// screen has a way to ask for one.
    /// </summary>
    public static bool OffersRegenerate(ChatMessage message, bool canAsk) =>
        canAsk && message is { FromPerson: false, Streaming: false, MayRegenerate: true };

    /// <summary>
    /// True when an unusable tool-request reply may be tried again through the existing regenerate flow.
    /// </summary>
    public static bool OffersUnusableRetry(ChatMessage message, bool canAsk) =>
        message.UnusableToolRequest && OffersRegenerate(message, canAsk);

    /// <summary>
    /// The messages of the newest reply, by their keys: the bot's last message and the bot's messages straight before
    /// it, back to what a person said. A message a person sent after it does not make it an older reply.
    /// </summary>
    public static IReadOnlySet<string> NewestReply(IReadOnlyList<ChatMessage> messages)
    {
        HashSet<string> keys = [];
        var at = messages.Count - 1;

        while (at >= 0 && messages[at].FromPerson)
        {
            at--;
        }

        for (; at >= 0 && !messages[at].FromPerson; at--)
        {
            keys.Add(messages[at].Key);
        }

        return keys;
    }

    /// <summary>
    /// A reply's actions (its footer, and the way to what happened) show on the newest reply, where they are needed,
    /// and on an older one only while a person has asked for them: by the keyboard's focus or by a press on it.
    /// </summary>
    public static bool ShowsActions(bool newest, bool revealed) => newest || revealed;

    /// <summary>
    /// An older reply that has actions to show keeps them out of sight until asked, and is then one stop for the
    /// keyboard that opens and closes. A person's message, words still arriving, and a message with nothing to offer
    /// are not.
    /// </summary>
    public static bool RevealsActions(ChatMessage message, bool newest, bool hasFooter, bool leadsToRun) =>
        !newest && message is { FromPerson: false, Streaming: false } && (hasFooter || leadsToRun);

    /// <summary>
    /// The last words of the newest reply once its run has finished, whoever it was written for: the reply the line
    /// under the composer is about. None while the newest reply still arrives.
    /// </summary>
    public static ChatMessage? NewestFinished(IReadOnlyList<ChatMessage> messages)
    {
        var newest = NewestReply(messages);
        return messages.LastOrDefault(message =>
            newest.Contains(message.Key)
            && message is { Streaming: false } and ({ FinishesReply: true } or { EndsReply: true }));
    }

    /// <summary>Said under a message sent while the bot works, until the bot has read it.</summary>
    public static string Pending(string botName) => $"Sent. {botName} reads it at its next step.";

    /// <summary>What a bot's reply says it is doing while it arrives: thinking until its words begin, then writing.</summary>
    public static string Arriving(ChatMessage message) => message.Thinking ? TurnLedger.Thinking : "Writing";

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

    #region Versions

    /// <summary>What an older reply's actions are called where their being shown or hidden is said.</summary>
    public static string ActionsName(string botName) => $"Actions for {botName}'s reply";

    public const string RegenerateLabel = "Write another reply";

    /// <summary>The edit icon's name under a person's last message.</summary>
    public const string EditMessageName = "Edit message";

    public const string SendEditLabel = "Send edit";

    public const string CancelEditLabel = "Cancel";

    public const string PreviousVersion = "Show the previous version";

    public const string NextVersion = "Show the next version";

    /// <summary>Where a version stands, as the switch shows it.</summary>
    public static string VersionLabel(VersionSwitch versions) => $"{versions.Number} of {versions.Count}";

    /// <summary>What the switch is read out as: what has versions, and which one is shown.</summary>
    public static string VersionName(VersionSwitch versions, bool fromPerson) =>
        $"{(fromPerson ? "Your message" : "Reply")}, version {versions.Number} of {versions.Count}";

    /// <summary>The field an edit is written in, named for whose message it changes.</summary>
    public static string EditFieldName(string botName) => $"Edit your message to {botName}";

    /// <summary>Said under the field: the edit takes the message's place and is answered again; nothing is lost.</summary>
    public static string EditHint(string botName) => $"{botName} answers the edit. The earlier version stays one step back.";

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
