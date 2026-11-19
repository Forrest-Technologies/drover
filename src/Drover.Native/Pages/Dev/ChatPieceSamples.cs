namespace Drover.Native.Pages;

/// <summary>
/// The gallery's samples of what a conversation shows and what opens beside one: tool calls in every phase, work
/// handed between bots, a bot asking for a person at its screen, files, and the panels. The handed work is the web
/// gallery's own sample. Every name, text, and value is a sample, and the gallery says so at its top.
/// </summary>
public static class ChatPieceSamples
{
    #region Private Fields

    private const string SampleBot = "Sample bot";

    private static readonly Guid sampleLead = Guid.Parse("00000000-0000-7000-8000-000000000001");

    private static readonly Guid sampleHelper = Guid.Parse("00000000-0000-7000-8000-000000000002");

    private static readonly DateTimeOffset sampleStart = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    private static readonly string[] sampleLine = ["Sample lead bot", "Sample helper", "Sample scout", "Sample runner"];

    private static readonly ChatMessage[] handoffs =
    [
        Said("sample-handoff-1", "A sample reply that hands work over.",
            Handed("00000000-0000-7000-8000-000000000211", DelegationPhase.WaitingForYou, 0, work:
                Said("sample-handoff-2", "A sample line of the helper's work.",
                    Call("00000000-0000-7000-8000-000000000212", "awaiting_approval")))),
        Said("sample-handoff-3", "A sample reply with finished, failed, and refused work.",
            Handed("00000000-0000-7000-8000-000000000213", DelegationPhase.Done, 0, answer: "A sample answer with **bold** text."),
            Handed("00000000-0000-7000-8000-000000000214", DelegationPhase.Failed, 0),
            Handed("00000000-0000-7000-8000-000000000215", DelegationPhase.Refused, 0, refusal: DelegationRefusal.Loop)),
        Said("sample-handoff-4", "A sample reply with work handed down three levels.",
            Handed("00000000-0000-7000-8000-000000000216", DelegationPhase.Working, 0, work:
                Said("sample-handoff-5", "A sample line of the helper's work.",
                    Handed("00000000-0000-7000-8000-000000000217", DelegationPhase.Working, 1, work:
                        Said("sample-handoff-6", "A sample line one level further down.",
                            Handed("00000000-0000-7000-8000-000000000218", DelegationPhase.Working, 2)))))),
    ];

    #endregion

    #region Public Methods

    public static IEnumerable<GallerySection> Sections() =>
    [
        new(
            "Tool call",
            "Closed, a card reads what the bot did, its arguments on one line, and its state in words. A line travels its top edge only while the tool runs, then lies still in the result's color. A call that waits is a question with two answers.",
            ToolCalls),
        new(
            "Work passing between bots",
            "A violet edge while the work is out, then the result's color. Inside: the other bot's own words and calls, and a call waiting for an answer. Past two levels a card links instead of nesting. Every name and text is a sample.",
            () => [Wide(Conversation(handoffs, sampleLine[0]))]),
        new(
            "A bot asks for a person at its screen",
            "Three moments, one card: the ask, the person holding the screen, and the answer given. Nothing moves while it waits.",
            Takeovers),
        new("File", "A picture keeps its shape while it loads, a text file shows its first lines, and every file has the same way to get it.", Files),
        new("Files panel", "The files a person and a bot exchanged, newest first. The filter appears only when files came from both sides.", FilesPanels),
        new("Memory panel", "What a bot remembers about the person reading. Each note says where it came from and is changed in place.", MemoryPanels),
        new("Conversation search", "The box above the crew, empty, with sample results, searching, finding nothing, and failing. A result opens its conversation at the message.", () => PanelSamples.Searches()),
        new("Summary panel", "The summary the server keeps of a conversation, and what it says when there is none.", () => PanelSamples.Summaries()),
        new("Screen panel", "The panel around a bot's screen: every state with no screen to show, and the sandbox's state above one that is. Here a labeled box stands where the screen is drawn.", () => PanelSamples.Screens()),
        new("Activity panel", "A run as a timeline, read from its stream. This sample run waits for a person, so nothing in it moves.", () => PanelSamples.Activities()),
    ];

    #endregion

    #region Tool Calls

    private static IEnumerable<UIElement> ToolCalls()
    {
        var command = """{"command":"sample --command"}""";
        var printed = View("00000000-0000-7000-8000-000000000301", "sandbox.run", command, "completed") with
        {
            Preview = """{"exit_code":0,"stdout":"sample output\nsecond sample line"}""",
        };
        var failed = View("00000000-0000-7000-8000-000000000302", "sandbox.run", command, "failed") with
        {
            Preview = """{"error":"A sample reason the command could not run."}""",
        };
        var nonzero = View("00000000-0000-7000-8000-000000000303", "sandbox.run", command, "completed") with
        {
            Preview = """{"exit_code":7,"stdout":"sample output","stderr":"sample error output"}""",
            Pieces = [new("stdout", "sample output\n"), new("stderr", "sample error output\n")],
        };
        var writes = View("00000000-0000-7000-8000-000000000306", "sandbox.write_file", """{"path":"/sample/notes.md","content":"A sample line."}""", "awaiting_approval");
        var makes = View(
            "00000000-0000-7000-8000-000000000307",
            "crew.add",
            """{"name":"Sample scout","description":"A sample purpose.","instructions":"A sample instruction.\nA second sample line."}""",
            "awaiting_approval") with
        {
            Details = [new(ToolApprovalRequested.ModelDetail, "sample-model"), new(ToolApprovalRequested.BeneathDetail, sampleLine[0])],
        };
        var noteId = Guid.Parse("00000000-0000-7000-8000-000000000309");
        var remembered = View("00000000-0000-7000-8000-000000000308", "memory.save", """{"text":"A sample note."}""", "completed") with
        {
            Preview = $$$"""{"note":{"id":"{{{noteId}}}","text":"A sample note."}}""",
        };

        var note = Card(remembered.ToCard());
        note.MemoryHref = PanelSamples.Here;
        note.Forget = (_, _) =>
        {
            note.Forgotten = true;
            return Task.FromResult(new ForgetOutcome(true));
        };

        return
        [
            Wide(Card(View("00000000-0000-7000-8000-000000000300", "sandbox.run", command, "running").ToCard())),
            Wide(Card(printed.ToCard())),
            Wide(Card(failed.ToCard())),
            Wide(Opened(Card(nonzero.ToCard()))),
            Wide(Card(View("00000000-0000-7000-8000-000000000304", "sandbox.run", command, "interrupted").ToCard())),
            Wide(Card(View("00000000-0000-7000-8000-000000000305", "sandbox.run", command, "denied").ToCard())),
            Wide(Card(writes.ToCard())),
            Wide(Card(makes.ToCard())),
            Wide(note),
        ];
    }

    private static ToolCallCard Card(ToolCard card, string botName = SampleBot) =>
        new() { Card = card, BotName = botName, Decide = (_, _) => Task.FromResult<string?>(null) };

    private static ToolCallView View(string id, string tool, string arguments, string state) =>
        new(Guid.Parse(id), tool, tool, arguments, "write", state, IsError: state is "failed");

    #endregion

    #region Handed Work

    // A stand-in for the conversation's own entry: a bot's words, then the cards of the calls it made with them.
    private static StackPanel Conversation(IEnumerable<ChatMessage> messages, string botName)
    {
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        foreach (var message in messages)
        {
            column.Children.Add(new MarkdownView { Text = message.Text });
            foreach (var tool in message.Tools)
            {
                column.Children.Add(tool.Delegation is { } work
                    ? new DelegationCard { Item = work, ChildContent = work.Work.Count > 0 ? Conversation(work.Work, work.ChildName) : null }
                    : Card(tool, botName));
            }
        }

        return column;
    }

    private static ChatMessage Said(string key, string text, params ToolCard[] tools) => new(key, false, text) { Tools = tools };

    private static ToolCard Call(string id, string state) =>
        new ToolCallView(Guid.Parse(id), "sandbox_run", "sandbox_run", """{"command":"sample command"}""", "write", state).ToCard();

    // A sample card for handed work: each level down is handed to the next sample bot in the line.
    private static ToolCard Handed(
        string id,
        DelegationPhase phase,
        int depth,
        ChatMessage? work = null,
        string? answer = null,
        DelegationRefusal refusal = DelegationRefusal.None) =>
        Call(id, "running") with
        {
            Delegation = new DelegationItem(
                Guid.Parse(id), sampleLine[depth], depth == 0 ? sampleHelper : Guid.Parse(id), sampleLine[depth + 1],
                "Sample task: find three sample sources.\nA second sample line.", phase, answer, refusal, PanelSamples.Here, depth)
            {
                Work = work is null ? [] : [work],
            },
        };

    #endregion

    #region Takeovers

    private static IEnumerable<UIElement> Takeovers()
    {
        var reason = "A sample reason: sign in to the sample site.";
        var call = new Takeover(Guid.Parse("00000000-0000-7000-8000-000000000401"), reason, TakeoverPhase.Asked);
        var takeovers = new ScreenTakeovers(null, ScreenAccess.NotPermitted, null, _ => Task.FromResult<string?>(null), (_, _, _) => Task.FromResult<string?>(null));

        return
        [
            Wide(new TakeoverCard { BotName = SampleBot, Reason = reason, Phase = TakeoverPhase.Asked }),
            Wide(new TakeoverCard { BotName = SampleBot, Reason = reason, Phase = TakeoverPhase.Asked, Access = ScreenAccess.Unknown }),
            Wide(new TakeoverCard { BotName = SampleBot, Reason = reason, Phase = TakeoverPhase.Asked, Access = ScreenAccess.Missing, SetupHref = PanelSamples.Here }),
            Wide(new TakeoverCard { BotName = sampleLine[1], Reason = reason, Phase = TakeoverPhase.Asked, Access = ScreenAccess.Elsewhere, SetupHref = PanelSamples.Here }),
            Wide(new TakeoverCall { Takeover = call, BotName = SampleBot, Takeovers = takeovers }),
            Wide(new TakeoverCard { BotName = SampleBot, Reason = reason, Phase = TakeoverPhase.Holding }),
            Wide(new TakeoverCard { BotName = SampleBot, Reason = reason, Phase = TakeoverPhase.Holding, AnswerBesideScreen = true }),
            Wide(new TakeoverCard { BotName = SampleBot, Reason = reason, Phase = TakeoverPhase.Holding, Sending = true, Problem = "A sample problem: your answer was not sent." }),
            Wide(new TakeoverCard { BotName = SampleBot, Reason = reason, Phase = TakeoverPhase.Done }),
            Wide(new TakeoverCard { BotName = SampleBot, Reason = reason, Phase = TakeoverPhase.CouldNot }),
        ];
    }

    #endregion

    #region Files

    private static IEnumerable<UIElement> Files() =>
    [
        Wide(PanelSamples.Picture()),
        Wide(new FileCard
        {
            File = Text("00000000-0000-7000-8000-000000000502", fromBot: true),
            ReadWhole = _ => Task.FromResult(new FileText("A sample first line.\nA sample second line.\nA sample third line, shown on Show all.", Cut: false)),
        }),
        Wide(new FileCard { File = Report("00000000-0000-7000-8000-000000000503", fromBot: true) with { Caption = "A sample caption." } }),
    ];

    private static IEnumerable<UIElement> FilesPanels()
    {
        SharedFile[] both = [Report("00000000-0000-7000-8000-000000000511", fromBot: true), Text("00000000-0000-7000-8000-000000000512", fromBot: false)];
        var failed = new FilesPanel { State = FilesPanelState.Failed, BotName = SampleBot, Problem = "A sample problem: the server did not answer." };
        failed.Retry += (_, _) => failed.State = FilesPanelState.Loading;

        return
        [
            Wide(new FilesPanel { State = FilesPanelState.Loading, BotName = SampleBot }),
            Wide(failed),
            Wide(new FilesPanel { State = FilesPanelState.Ready, BotName = SampleBot }),
            Wide(new FilesPanel { State = FilesPanelState.Ready, BotName = SampleBot, Files = both }),
        ];
    }

    private static SharedFile Text(string id, bool fromBot) =>
        new(Guid.Parse(id), "sample-notes.txt", "text/plain", 5_000, PanelSamples.Here, fromBot, sampleStart, PanelSamples.Here, Preview: "A sample first line.\nA sample second line.");

    private static SharedFile Report(string id, bool fromBot) =>
        new(Guid.Parse(id), "sample-report.pdf", "application/pdf", 3_400_000, PanelSamples.Here, fromBot, sampleStart.AddDays(-1), PanelSamples.Here);

    #endregion

    #region Memory

    private static IEnumerable<UIElement> MemoryPanels()
    {
        MemoryNote[] notes =
        [
            new(Guid.Parse("00000000-0000-7000-8000-000000000601"), "A sample note the bot wrote.", true, sampleStart, PanelSamples.Here),
            new(Guid.Parse("00000000-0000-7000-8000-000000000602"), "A sample note the person wrote.", false, sampleStart.AddDays(-1), null),
        ];
        var failed = new MemoryPanel { State = MemoryPanelState.Failed, BotName = SampleBot, Problem = "A sample problem: the server did not answer." };
        failed.Retry += (_, _) => failed.State = MemoryPanelState.Loading;
        var ready = new MemoryPanel { State = MemoryPanelState.Ready, BotName = SampleBot, Notes = notes, NearlyFull = true };
        ready.OnAdd = _ => Task.FromResult<string?>("A sample problem: the note was not saved.");
        ready.OnChange = (_, _) => Task.FromResult<string?>(null);
        ready.Remove += (_, id) => ready.Notes = [.. ready.Notes.Where(note => note.Id != id)];

        return
        [
            Wide(new MemoryPanel { State = MemoryPanelState.Loading, BotName = SampleBot }),
            Wide(failed),
            Wide(new MemoryPanel { State = MemoryPanelState.Off, BotName = SampleBot, Notes = notes[..1], EditorHref = PanelSamples.Here }),
            Wide(new MemoryPanel { State = MemoryPanelState.Ready, BotName = SampleBot }),
            Wide(ready),
        ];
    }

    #endregion

    #region Private Methods

    private static T Wide<T>(T element)
        where T : UIElement
    {
        FlowPanel.SetFullRow(element, true);
        return element;
    }

    // Opens a card once it is on the page, as a person pressing it would, so the gallery shows what an open one holds.
    private static T Opened<T>(T card)
        where T : FrameworkElement
    {
        card.Loaded += (_, _) => card.DispatcherQueue.TryEnqueue(() =>
        {
            if (Find<Disclosure>(card) is { } section)
            {
                section.Open = true;
            }
        });
        return card;
    }

    private static T? Find<T>(DependencyObject root)
        where T : class
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if ((child as T ?? Find<T>(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    #endregion
}
