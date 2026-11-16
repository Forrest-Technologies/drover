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

    private const string ImageKind = "image";

    private const string FileKind = "file";

    private const string DelegateTool = "delegate";

    #endregion

    #region Public Methods

    /// <summary>
    /// The messages a person reads: what they wrote and what the bot said, including the reply still arriving, and
    /// after the bot's words the tool calls it made with them. <paramref name="tools"/> are the calls this screen
    /// knows from the thread's past runs and from <paramref name="run"/>, the run being watched; a call the stored
    /// messages do not have yet (the bot has not finished the reply that makes it) is shown after that run's newest
    /// message. A call the screen has no events for is shown as the stored messages tell it. A message's files are
    /// shown at the address <paramref name="fileAddress"/> gives each; without one, they are left out. With a
    /// <paramref name="handover"/>, a call that handed work to another bot is shown as that work, with the other bot's
    /// own activity inside it; without one, as any other call.
    /// </summary>
    public static IReadOnlyList<ChatMessage> ToChatMessages(
        this IEnumerable<MessageView> messages,
        IReadOnlyDictionary<Guid, ToolCallView>? tools = null,
        RunView? run = null,
        Func<Guid, string>? fileAddress = null,
        Handover? handover = null)
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
            List<ToolCard> cards = [.. CardsOf(message, tools, results, handover)];

            if (message.Id == host)
            {
                cards.AddRange(unplaced.Select(tool => tool.ToCard().Handed(tool, null, handover)));
                unplaced.Clear();
            }

            conversation.Add(ToChatMessage(message, cards, fileAddress));
        }

        conversation.RemoveAll(message => message.Text.Length == 0 && message.Tools.Count == 0 && message.Files.Count == 0);
        AddUnplaced(conversation, unplaced, run, handover);
        return conversation;
    }

    /// <summary>Every tool call the conversation shows.</summary>
    public static IEnumerable<ToolCard> AllTools(this IEnumerable<ChatMessage> messages) =>
        messages.SelectMany(message => message.Tools);

    /// <summary>
    /// Every tool call the conversation shows, with the bot that made it: <paramref name="botName"/>'s own, and inside
    /// work it handed over, the calls of the bot that took it, at every depth shown.
    /// </summary>
    public static IEnumerable<(ToolCard Card, string BotName)> AllTools(this IEnumerable<ChatMessage> messages, string botName) =>
        messages.SelectMany(message => message.Tools).SelectMany(card => card.Delegation is { } work
            ? work.Work.AllTools(work.ChildName).Prepend((card, botName))
            : [(card, botName)]);

    /// <summary>
    /// True while the bot works on what it says next: none of its words have arrived yet, no tool of its is under way,
    /// and it is not waiting for a person.
    /// </summary>
    public static bool IsThinking(this RunView run) =>
        run.IsActive
        && run.Status is not "waiting"
        && !run.Messages.Any(message => message.IsStreaming && Words(message).Length > 0)
        && !run.ToolCalls.Any(tool => tool.State is "requested" or "approved" or "running");

    /// <summary>
    /// The bots a run has handed work to, at any depth, and where that work stands. For a bot with several pieces, work
    /// it is working on wins over work waiting for a person, which wins over work that came back. Work whose run ended
    /// without reporting back counts as come back: nothing is out any more.
    /// </summary>
    public static ImmutableDictionary<Guid, Handoff> Handoffs(this RunView run) =>
        Delegations(run)
            .GroupBy(work => work.ChildBotId)
            .ToImmutableDictionary(group => group.Key, group => group.Min(StateOf));

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

    private static ChatMessage ToChatMessage(MessageView message, List<ToolCard> cards, Func<Guid, string>? fileAddress)
    {
        var fromPerson = message.Role is PersonRole;
        var shown = new ChatMessage(message.Id.ToString(), fromPerson, Words(message), message.IsStreaming, message.IsPending)
        {
            Files = fileAddress is null
                ? []
                :
                [
                    .. message.Parts
                        .Where(part => part.Kind is ImageKind or FileKind && part.File is not null)
                        .Select(part => FileWords.Shared(part.File!, fileAddress(part.File!.Id), fromBot: !fromPerson)),
                ],
        };
        return cards.Count == 0 ? shown : shown with { Tools = cards };
    }

    // A call part without an id cannot be followed, so it is left out.
    private static IEnumerable<ToolCard> CardsOf(
        MessageView message,
        IReadOnlyDictionary<Guid, ToolCallView>? tools,
        Dictionary<Guid, PartView> results,
        Handover? handover) =>
        message.Parts
            .Where(part => part.Kind is ToolCallKind && part.ToolCallId is not null)
            .Select(part =>
            {
                var result = results.GetValueOrDefault(part.ToolCallId!.Value);
                var call = tools?.GetValueOrDefault(part.ToolCallId.Value) ?? ToolCards.FromParts(part, result);
                return call.ToCard(result).Handed(call, result, handover);
            });

    // Calls of the run whose reply has no message on screen yet go after the last message that is no longer pending,
    // where the reply will appear.
    private static void AddUnplaced(List<ChatMessage> conversation, List<ToolCallView> unplaced, RunView? run, Handover? handover)
    {
        if (unplaced.Count == 0 || run is null)
        {
            return;
        }

        var at = conversation.FindLastIndex(message => !message.Pending) + 1;
        var turn = new ChatMessage($"tools-{run.RunId}", false, string.Empty)
        {
            Tools = [.. unplaced.Select(tool => tool.ToCard().Handed(tool, null, handover))],
        };
        conversation.Insert(at, turn);
    }

    #endregion

    #region Handed Work

    // A call that handed work over is shown as that work once the server began it, or as the refusal when it would
    // not. Before either (it waits for the person's approval, or they declined), it is a call like any other.
    private static ToolCard Handed(this ToolCard card, ToolCallView call, PartView? result, Handover? handover)
    {
        if (handover is null || call.ToolName != DelegateTool)
        {
            return card;
        }

        var arguments = JsonElement.Parse(call.Arguments);
        var childId = Guid.TryParse(Text(arguments, "bot_id"), out var id) ? id : Guid.Empty;
        var child = call.Delegation?.ChildBotId ?? childId;
        var childName = handover.NameOf(child);
        var task = Text(arguments, "task") ?? string.Empty;
        var refusal = call.Delegation is null && call.State is not "denied" && (call.IsError || call.State is "refused" || result?.IsError is true)
            ? DelegationWords.RefusalIn(call.Preview) ?? DelegationWords.RefusalIn(result?.Result)
            : null;

        if (call.Delegation is null && refusal is null)
        {
            return card with { Facts = [new("Bot", childName), new("Task", task)] };
        }

        var item = new DelegationItem(
            call.ToolCallId,
            handover.BotName,
            child,
            childName,
            task,
            refusal is null ? PhaseOf(call) : DelegationPhase.Refused,
            call.Delegation is { Outcome: "completed", Answer: { } answer } ? answer : null,
            refusal ?? DelegationRefusal.None,
            call.Delegation is { ChildThreadId: { } thread } ? handover.WorkAddress?.Invoke(child, thread) : null,
            handover.Depth);
        return card with { Delegation = Answered(item with { Work = WorkOf(call.Delegation, handover.Below(childName)) }) };
    }

    // The answer a parent was given is the child's last reply, cut to its start. Where the work shows that reply, it is
    // shown once, whole, as the answer.
    private static DelegationItem Answered(DelegationItem item) =>
        item is { Answer: { Length: > 0 } answer, Work: [.., { FromPerson: false, Tools.Count: 0 } last] }
        && last.Text.StartsWith(answer.Trim(), StringComparison.Ordinal)
            ? item with { Answer = last.Text, Work = [.. item.Work.Take(item.Work.Count - 1)] }
            : item;

    private static DelegationPhase PhaseOf(ToolCallView call) => call.Delegation switch
    {
        { Outcome: "completed" } => DelegationPhase.Done,
        { Outcome: "failed" } => DelegationPhase.Failed,
        { Outcome: not null } => DelegationPhase.Cancelled,
        _ when !call.IsOpen => DelegationPhase.Cancelled,
        { Child: var child } when Waits(child) => DelegationPhase.WaitingForYou,
        _ => DelegationPhase.Working,
    };

    private static IEnumerable<DelegationView> Delegations(RunView run) =>
        run.ToolCalls
            .Select(tool => tool.Delegation)
            .OfType<DelegationView>()
            .SelectMany(work => Delegations(work.Child).Prepend(work));

    private static Handoff StateOf(DelegationView work) => work switch
    {
        { IsOut: false } or { Child.IsActive: false } => Handoff.Returned,
        _ when Waits(work.Child) => Handoff.Waiting,
        _ => Handoff.Working,
    };

    // A child waits for a person when a call of its, or of a bot it handed work to, does.
    private static bool Waits(RunView run) =>
        run.Status is "waiting" || run.ToolCalls.Any(tool => tool.State is "awaiting_approval" || (tool.Delegation is { } work && Waits(work.Child)));

    // The child's own words and calls, shown as the conversation shows a bot's. Past the deepest level shown, the card
    // links to the child's side instead.
    private static IReadOnlyList<ChatMessage> WorkOf(DelegationView? work, Handover below) =>
        work is null || below.Depth > DelegationWords.DeepestShown
            ? []
            : ChatTimeline.Merge([], work.Child)
                .ToChatMessages(work.Child.ToolCalls.ToImmutableDictionary(tool => tool.ToolCallId), work.Child, handover: below);

    private static string? Text(JsonElement values, string name) =>
        values.ValueKind is JsonValueKind.Object && values.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Words(MessageView message) =>
        string.Join("\n\n", message.Parts.Where(part => part.Kind is TextKind && !string.IsNullOrWhiteSpace(part.Text)).Select(part => part.Text));

    #endregion
}
