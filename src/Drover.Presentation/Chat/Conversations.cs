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

    private const string ReasoningKind = "reasoning";

    private const string SourceKind = "source";

    private const string ToolCallKind = "tool_call";

    private const string ToolResultKind = "tool_result";

    private const string ImageKind = "image";

    private const string FileKind = "file";

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
    /// own activity inside it; without one, as any other call. What a finished reply used is offered only to a
    /// <paramref name="reader"/> the server will answer; without one, nowhere.
    /// </summary>
    public static IReadOnlyList<ChatMessage> ToChatMessages(
        this IEnumerable<MessageView> messages,
        IReadOnlyDictionary<Guid, ToolCallView>? tools = null,
        RunView? run = null,
        Func<Guid, string>? fileAddress = null,
        Handover? handover = null,
        UsageReader? reader = null)
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

            conversation.Add(ToChatMessage(message, cards, fileAddress) with { RunId = RunOf(message, run) });
        }

        conversation.RemoveAll(message => message is { Text.Length: 0, Tools.Count: 0, Files.Count: 0, Reasoning: null, Sources.Count: 0 });
        var principals = shown.Where(message => message.RunPrincipalUserId is not null)
            .ToDictionary(message => message.Id.ToString(), message => message.RunPrincipalUserId);
        MarkReplyEnds(conversation, run, entry => reader?.MayRead(principals.GetValueOrDefault(entry.Key)) ?? false);

        AddUnplaced(conversation, unplaced, run, handover);
        PlaceVersions(conversation, shown);
        return LeadToRuns(conversation, shown);
    }

    /// <summary>
    /// The conversation with a new version offered where it may be asked for: the person may edit their last message
    /// and ask for another reply to it, at the reply's last words, while nothing works on the conversation and
    /// nothing waits to be sent. While something does, the versions shown cannot be switched either.
    /// </summary>
    public static IReadOnlyList<ChatMessage> OfferingVersions(this IReadOnlyList<ChatMessage> conversation, bool idle)
    {
        var lastAsked = conversation.ToList().FindLastIndex(message => message is { FromPerson: true, Pending: false });
        var lastReply = conversation.ToList().FindLastIndex(message => message is { FromPerson: false, Streaming: false, Text.Length: > 0 });
        var free = idle && !conversation.Any(message => message.Pending);

        return
        [
            .. conversation.Select((message, index) => message with
            {
                MayEdit = free && index == lastAsked && Guid.TryParse(message.Key, out _),
                MayRegenerate = free && index == lastReply && index > lastAsked && lastAsked >= 0 && Guid.TryParse(message.Key, out _),
                Versions = message.Versions is { } versions && versions.Enabled != free ? versions with { Enabled = free } : message.Versions,
            }),
        ];
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
        "waiting" when run.Reason is not ("awaiting_approval" or "awaiting_takeover") =>
            "The bot is waiting for an answer this screen cannot ask for yet. Stop the reply to carry on.",
        _ => null,
    };

    #endregion

    #region Private Methods

    private static ChatMessage ToChatMessage(MessageView message, List<ToolCard> cards, Func<Guid, string>? fileAddress)
    {
        var fromPerson = message.Role is PersonRole;
        var words = Words(message);
        List<SharedFile> files = fileAddress is null
            ? []
            :
            [
                .. message.Parts
                    .Where(part => part.Kind is ImageKind or FileKind && part.File is not null)
                    .Select(part => FileWords.Shared(part.File!, fileAddress(part.File!.Id), fromBot: !fromPerson)),
            ];
        List<SourceLink> sources = fromPerson ? [] : [.. Sources(message)];
        var unusableToolRequest = !fromPerson
            && !message.IsStreaming
            && cards.Count == 0
            && files.Count == 0
            && sources.Count == 0
            && UnusableToolRequests.IsUnusableRequest(words);

        var shown = new ChatMessage(message.Id.ToString(), fromPerson, words, message.IsStreaming, message.IsPending)
        {
            SentBy = SentBy(message),
            Reasoning = fromPerson ? null : Reasoning(message),
            Model = fromPerson ? null : message.Model,
            UnusableToolRequest = unusableToolRequest,
        };
        shown = files.Count == 0 ? shown : shown with { Files = files };
        shown = sources.Count == 0 ? shown : shown with { Sources = sources };
        return cards.Count == 0 ? shown : shown with { Tools = cards };
    }

    // Reasoning parts next to each other are pieces of one thought, which a reply saved before they were stored whole
    // can still hold one by one, so they read as one text. Reasoning on either side of anything else is two thoughts.
    private static string? Reasoning(MessageView message)
    {
        List<string> thoughts = [];
        var piecing = false;
        foreach (var part in message.Parts)
        {
            if (part.Kind is not ReasoningKind)
            {
                piecing = false;
                continue;
            }

            if (piecing)
            {
                thoughts[^1] = $"{thoughts[^1]}{part.Text}";
            }
            else
            {
                thoughts.Add(part.Text ?? string.Empty);
                piecing = true;
            }
        }

        List<string> shown = [.. thoughts.Where(thought => !string.IsNullOrWhiteSpace(thought))];
        return shown.Count > 0 ? string.Join("\n\n", shown) : null;
    }

    // A source cited twice is shown once, and one that is not a web address is not shown at all.
    private static IEnumerable<SourceLink> Sources(MessageView message) =>
        message.Parts
            .Where(part => part.Kind is SourceKind)
            .Select(part => SourceLink.From(part.Url, part.Text))
            .OfType<SourceLink>()
            .DistinctBy(source => source.Address);

    // A person's message switches between its own versions. A reply's versions are the first messages of each reply
    // to the same message, so the switch stands at the end of the reply shown: its last words, or its last message.
    private static void PlaceVersions(List<ChatMessage> conversation, List<MessageView> shown)
    {
        for (var index = 0; index < shown.Count; index++)
        {
            if (shown[index] is not { Versions: { } versions } message || VersionSwitch.For(message.Id, versions) is not { } place)
            {
                continue;
            }

            var at = message.Role is PersonRole
                ? conversation.FindIndex(entry => entry.Key == message.Id.ToString())
                : EndOfReply(conversation, [.. shown.Skip(index).TakeWhile(each => each.Role is BotRole).Select(each => each.Id.ToString())]);

            if (at >= 0)
            {
                conversation[at] = conversation[at] with { Versions = place };
            }
        }
    }

    private static int EndOfReply(List<ChatMessage> conversation, HashSet<string> reply)
    {
        var lastWords = conversation.FindLastIndex(entry => reply.Contains(entry.Key) && entry.Text.Length > 0);
        return lastWords >= 0 ? lastWords : conversation.FindLastIndex(entry => reply.Contains(entry.Key));
    }

    // Each run is reached from its last reply that has words, so a run of many steps is offered once, at its end.
    private static List<ChatMessage> LeadToRuns(List<ChatMessage> conversation, IEnumerable<MessageView> shown)
    {
        var runs = shown.Where(message => message.RunId is not null).ToDictionary(message => message.Id.ToString(), message => message.RunId!.Value);
        HashSet<Guid> offered = [];
        for (var index = conversation.Count - 1; index >= 0; index--)
        {
            if (conversation[index] is { FromPerson: false, Text.Length: > 0 } reply
                && runs.TryGetValue(reply.Key, out var runId)
                && offered.Add(runId))
            {
                conversation[index] = reply with { ActivityRunId = runId };
            }
        }

        return conversation;
    }

    /// <summary>Who sent a message the person did not type: the routine, by its time, or one removed since.</summary>
    private static string? SentBy(MessageView message) => message switch
    {
        { ScheduleId: null } => null,
        { ScheduleCron: { } cron } => $"Sent by the routine '{RoutineWords.Describe(cron) ?? cron}'",
        _ => "Sent by a routine that has since been removed",
    };

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

    private static Guid? RunOf(MessageView message, RunView? run) => message switch
    {
        { Role: PersonRole } => null,
        { RunId: { } runId } => runId,
        _ when run is not null && run.Messages.Any(own => own.Id == message.Id) => run.RunId,
        _ => null,
    };

    // A reply finishes at its last words, once the run that wrote them has finished: there its footer stands, for
    // everyone who sees it. What the reply used is offered there only to someone who may read it.
    private static void MarkReplyEnds(List<ChatMessage> conversation, RunView? run, Func<ChatMessage, bool> readable)
    {
        var ends = conversation.Select((message, index) => (message, index))
            .Where(entry => entry.message is { FromPerson: false, RunId: not null } && entry.message.Text.Length > 0)
            .Where(entry => run is not { IsActive: true } || entry.message.RunId != run.RunId)
            .GroupBy(entry => entry.message.RunId)
            .Select(group => group.Last().index)
            .ToList();

        foreach (var index in ends)
        {
            conversation[index] = conversation[index] with { FinishesReply = true, EndsReply = readable(conversation[index]) };
        }
    }

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
        if (handover is null || call.ToolName is not ("delegate" or "message_bot"))
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

        var refusalLimit = refusal is DelegationRefusal.TooManyHandoffs
            ? DelegationWords.RefusalLimitIn(call.Preview) ?? DelegationWords.RefusalLimitIn(result?.Result)
            : null;
        var limitsHref = refusal is DelegationRefusal.TooManyHandoffs && handover.BotId != Guid.Empty
            ? handover.LimitsAddress?.Invoke(handover.BotId)
            : null;

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
            handover.Depth)
        {
            RefusalLimit = refusalLimit,
            LimitsHref = limitsHref,
        };
        return card with { Delegation = Answered(item with { Work = WorkOf(call.Delegation, handover.Below(childName, child)) }) };
    }

    // The answer a parent was given is the child's last reply, cut to its start. Where the work shows that reply, it is
    // shown once, whole, as the answer.
    private static DelegationItem Answered(DelegationItem item) =>
        item is { Answer: { Length: > 0 } answer, Work: [.., { FromPerson: false, Tools.Count: 0, UnusableToolRequest: false } last] }
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
