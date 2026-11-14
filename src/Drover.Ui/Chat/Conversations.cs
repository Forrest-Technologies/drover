namespace Drover.Ui.Chat;

/// <summary>
/// Turns what the client holds about a thread and its run into what the conversation shows.
/// </summary>
public static class Conversations
{
    #region Private Fields

    private const string PersonRole = "user";

    private const string BotRole = "assistant";

    private const string TextKind = "text";

    private const string ToolCallKind = "tool_call";

    private const string ToolResultKind = "tool_result";

    #endregion

    #region Public Methods

    /// <summary>
    /// The messages a person reads: what they wrote and what the bot said, including the reply still arriving, and
    /// after the bot's words the tool calls it made with them. <paramref name="tools"/> are the calls this screen
    /// knows from the thread's past runs and from <paramref name="run"/>, the run being watched; a call the stored
    /// messages do not have yet (the bot has not finished the reply that makes it) is shown after that run's newest
    /// message. A call the screen has no events for is shown as the stored messages tell it.
    /// </summary>
    public static IReadOnlyList<ChatMessage> ToChatMessages(
        this IEnumerable<MessageView> messages,
        IReadOnlyDictionary<Guid, ToolCallView>? tools = null,
        RunView? run = null)
    {
        List<MessageView> shown = [.. messages.Where(message => message.Role is PersonRole or BotRole)];
        var all = messages.SelectMany(message => message.Parts).ToList();
        var results = all.Where(part => part.Kind is ToolResultKind && part.ToolCallId is not null)
            .GroupBy(part => part.ToolCallId!.Value)
            .ToDictionary(group => group.Key, group => group.Last());
        var placed = all.Where(part => part.Kind is ToolCallKind && part.ToolCallId is not null)
            .Select(part => part.ToolCallId!.Value)
            .ToHashSet();
        List<ToolCallView> unplaced = [.. (run?.ToolCalls ?? []).Where(tool => !placed.Contains(tool.ToolCallId))];
        var host = run?.Messages.LastOrDefault(message => message.Role is BotRole)?.Id;

        List<ChatMessage> conversation = [];

        foreach (var message in shown)
        {
            List<ToolCard> cards = [.. CardsOf(message, tools, results)];

            if (message.Id == host)
            {
                cards.AddRange(unplaced.Select(tool => tool.ToCard()));
                unplaced.Clear();
            }

            conversation.Add(ToChatMessage(message, cards));
        }

        conversation.RemoveAll(message => message.Text.Length == 0 && message.Tools.Count == 0);
        AddUnplaced(conversation, unplaced, run);
        return conversation;
    }

    /// <summary>Every tool call the conversation shows.</summary>
    public static IEnumerable<ToolCard> AllTools(this IEnumerable<ChatMessage> messages) =>
        messages.SelectMany(message => message.Tools);

    /// <summary>
    /// True while the bot works on what it says next: none of its words have arrived yet, no tool of its is under way,
    /// and it is not waiting for a person.
    /// </summary>
    public static bool IsThinking(this RunView run) =>
        run.IsActive
        && run.Status is not "waiting"
        && !run.Messages.Any(message => message.IsStreaming && Words(message).Length > 0)
        && !run.ToolCalls.Any(tool => tool.State is "requested" or "approved" or "running");

    /// <summary>Why a run ended without an answer and what to do, or nothing when it did not fail.</summary>
    public static string? Problem(this RunView run) => run.Status is "failed"
        ? $"{run.Error?.Message ?? "The bot stopped before it could answer."} Send your message again to retry."
        : null;

    /// <summary>Something the person should know about a run that is not a failure.</summary>
    public static string? Notice(this RunView run) => run.Status switch
    {
        "cancelled" => "You stopped this reply.",
        "waiting" when run.Reason is not "awaiting_approval" =>
            "The bot is waiting for an answer this screen cannot ask for yet. Stop the reply to carry on.",
        _ => null,
    };

    #endregion

    #region Private Methods

    private static ChatMessage ToChatMessage(MessageView message, List<ToolCard> cards)
    {
        var shown = new ChatMessage(message.Id.ToString(), message.Role is PersonRole, Words(message), message.IsStreaming, message.IsPending);
        return cards.Count == 0 ? shown : shown with { Tools = cards };
    }

    // A call part without an id cannot be followed, so it is left out.
    private static IEnumerable<ToolCard> CardsOf(
        MessageView message,
        IReadOnlyDictionary<Guid, ToolCallView>? tools,
        Dictionary<Guid, PartView> results) =>
        message.Parts
            .Where(part => part.Kind is ToolCallKind && part.ToolCallId is not null)
            .Select(part =>
            {
                var result = results.GetValueOrDefault(part.ToolCallId!.Value);
                var call = tools?.GetValueOrDefault(part.ToolCallId.Value) ?? ToolCards.FromParts(part, result);
                return call.ToCard(result);
            });

    // Calls of the run whose reply has no message on screen yet go after the last message that is no longer pending,
    // where the reply will appear.
    private static void AddUnplaced(List<ChatMessage> conversation, List<ToolCallView> unplaced, RunView? run)
    {
        if (unplaced.Count == 0 || run is null)
        {
            return;
        }

        var at = conversation.FindLastIndex(message => !message.Pending) + 1;
        var turn = new ChatMessage($"tools-{run.RunId}", false, string.Empty) { Tools = [.. unplaced.Select(tool => tool.ToCard())] };
        conversation.Insert(at, turn);
    }

    private static string Words(MessageView message) =>
        string.Join("\n\n", message.Parts.Where(part => part.Kind is TextKind && !string.IsNullOrWhiteSpace(part.Text)).Select(part => part.Text));

    #endregion
}
