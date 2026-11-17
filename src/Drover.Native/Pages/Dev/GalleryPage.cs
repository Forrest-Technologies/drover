namespace Drover.Native.Pages;

/// <summary>
/// Every component in both themes, in every state: the bench the native controls are compared on against the web
/// client's gallery at the same address. All names, text, and values on it are samples. It is shown only in a
/// development build.
/// </summary>
public sealed class GalleryPage : Screen
{
    #region Private Fields

    private const string Markdown = """
        ### Sample answer

        A bot message with **bold**, `inline code`, and a [link](https://example.org).

        - [x] Read the page
        - [ ] Draft a reply

        | Step | Result |
        | --- | --- |
        | Fetch | Done |

        ![A sample image](https://example.org/sample.png)
        """;

    private const string Command = "docker compose up --build";

    private static readonly TabItem[] tabs = [new("model", "Model"), new("tools", "Tools"), new("memory", "Memory")];

    private static readonly SegmentOption<string>[] access = [new("off", "Off"), new("ask", "Ask"), new("allow", "Allow")];

    private static readonly SelectOption[] models = [new("a", "Sample model A"), new("b", "Sample model B")];

    private static readonly Guid sampleLead = Guid.Parse("00000000-0000-7000-8000-000000000001");

    private static readonly Guid sampleHelper = Guid.Parse("00000000-0000-7000-8000-000000000002");

    private static readonly DateTimeOffset sampleStart = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    private static readonly TimelineModel run = Timelines.Build(
        [new(sampleLead, "Sample lead bot"), new(sampleHelper, "Sample helper bot")],
        [
            Sample(1, sampleLead, TimelineKind.Step, "Thought", TimelineState.Done, 0, 3),
            Sample(2, sampleLead, TimelineKind.Delegation, "Handed work", TimelineState.Done, 3, 30, "Sample task", sampleHelper),
            Sample(3, sampleHelper, TimelineKind.Tool, "Ran a command", TimelineState.Done, 5, 12, "sample --command"),
            Sample(4, sampleHelper, TimelineKind.Tool, "Wrote a file", TimelineState.Failed, 18, 1, "/sample/path.txt"),
            Sample(5, sampleLead, TimelineKind.Answer, "Got the answer", TimelineState.Done, 33, null, null, sampleHelper),
            Sample(6, sampleLead, TimelineKind.Waiting, "Asked to run a command", TimelineState.Waiting, 36, null, "sample --other"),
            Sample(7, sampleLead, TimelineKind.Tool, "Reading a file", TimelineState.Live, 40, null, "/sample/notes.md"),
        ]);

    #endregion

    #region Constructors

    public GalleryPage()
    {
        var services = ((App)Application.Current).Services;
        if (!services.GetRequiredService<UiEnvironment>().IsDevelopment)
        {
            Content = new NotFound();
            return;
        }

        var toasts = services.GetRequiredService<ToastService>();
        var column = new StackPanel
        {
            Spacing = Tokens.Number("DvSpace7"),
            MaxWidth = (2 * Tokens.Number("DvDialogWidth")) + Tokens.Number("DvSpace7"),
            Padding = Tokens.Space("DvSpace4", "DvSpace6"),
        };

        column.Children.Add(Header());
        foreach (var section in Sections(toasts))
        {
            column.Children.Add(section);
        }

        Content = new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsTabStop = false };
    }

    #endregion

    #region Properties

    public override string Title => "Component gallery";

    #endregion

    #region Sections

    private static StackPanel Header()
    {
        var header = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        header.Children.Add(Styles.Text("Component gallery", "DvTextH1"));
        header.Children.Add(Styles.Text("Every component in both themes. All names, text, and values on this page are samples.", "DvTextSecondary"));
        header.Children.Add(new Badge { Text = "Sample content" });
        return header;
    }

    private static IEnumerable<GallerySection> Sections(ToastService toasts) =>
    [
        new("Button", "Primary is the one thing a person came to do. One per view.", () =>
        [
            new Button { Kind = ButtonKind.Primary, Content = "Connect a model" },
            new Button { Content = "Test connection" },
            new Button { Kind = ButtonKind.Danger, Content = "Remove bot" },
            new Button { Content = "Save", IsEnabled = false },
        ]),
        new("Icon button", "Each has a name that says what it does.", () =>
        [
            new IconButton { Icon = IconName.Plus, Label = "Add a bot" },
            new IconButton { Icon = IconName.Settings, Label = "Open settings" },
            new IconButton { Icon = IconName.Close, Label = "Close panel" },
            new IconButton { Icon = IconName.Remove, Label = "Remove bot", IsEnabled = false },
        ]),
        new("Field", null, () =>
        [
            new Field { Label = "Base address", Placeholder = "https://", Hint = "Where the model answers.", MinWidth = Tokens.Number("DvTooltipWidth") },
            new Field { Label = "Key", Type = "password", Value = "sample", Error = "The key was rejected. Check it and try again.", MinWidth = Tokens.Number("DvTooltipWidth") },
            new TextArea { Label = "Instructions", Placeholder = "What this sample bot is for.", Hint = "A sample hint.", MinWidth = Tokens.Number("DvTooltipWidth") },
        ]),
        new("Select, switch, segmented control", null, () =>
        [
            new Select { Label = "Model", Options = models, Value = "a", MinWidth = Tokens.Number("DvMenuWidth") },
            new Switch { Label = "Remember conversations", Value = true },
            new SegmentedControl { Label = "Tool access", Options = access, Value = "ask" },
        ]),
        new("Avatar", "The head carries the bot's hue. The visor is dark at rest, and lights only while the bot works.", () =>
        [
            new Avatar { BotName = "Sample bot", Hue = 0, Size = 96 },
            new Avatar { BotName = "Sample bot", Hue = 3, Size = 48 },
            new Avatar { BotName = "Sample bot", Hue = 6, Size = 32 },
            new Avatar { BotName = "Sample bot", Hue = 8, Size = 24 },
            new Avatar { BotName = "Sample bot", Hue = 10, Size = 20 },
            new Avatar { BotName = "Sample bot", Hue = 6, Size = 48, State = AvatarState.Thinking },
            new Avatar { BotName = "Sample bot", Hue = 6, Size = 48, State = AvatarState.Writing },
            new Avatar { BotName = "Sample bot", Hue = 6, Size = 48, State = AvatarState.Tool },
            new Avatar { BotName = "Sample bot", Hue = 6, Size = 48, State = AvatarState.Failed },
        ]),
        new("Badge", "The words carry the state. Color only adds to them.", () =>
        [
            new Badge { Text = "Idle" },
            new Badge { Text = "Thinking", Kind = BadgeKind.Live },
            new Badge { Text = "Done", Kind = BadgeKind.Ok },
            new Badge { Text = "Waiting for you", Kind = BadgeKind.Warn },
            new Badge { Text = "Failed", Kind = BadgeKind.Danger },
        ]),
        new("Tabs", null, () => [new Tabs { Label = "Sample sections", Items = tabs, PanelFor = tab => Styles.Text($"Sample panel for {tab.Label}.") }]),
        new("Empty state", "It always says why it is empty and offers one thing to do.", () =>
        [
            new EmptyState
            {
                Title = "No model yet",
                Cause = "A bot needs a model to answer. None is connected.",
                ActionLabel = "Connect a model",
                ActionHref = "/dev/gallery",
            },
        ]),
        new(
            "Dialog, sheet, menu, tooltip, toast",
            "Escape closes each one and focus returns to the control that opened it.",
            () => OverlaySamples.Make(toasts)),
        new("Markdown", "Bot messages render through this. Raw HTML is dropped and a remote image waits for a click.", () =>
        [
            new MarkdownView { Text = Markdown },
        ]),
        new("Status note", "What happened and what to do, said in a sentence. Failures are announced at once.", () =>
        [
            new StatusNote { Kind = NoteKind.Ok, Text = "Connected. The sample endpoint lists 2 models." },
            new StatusNote { Kind = NoteKind.Danger, Text = "The sample provider rejected the key. Check the key and test again." },
            new StatusNote { Text = "A sample note with nothing to act on." },
        ]),
        new("Setup steps", null, () => [new SetupSteps { Current = 2, MinWidth = Tokens.Number("DvMenuWidth") }]),
        new("Disclosure", "Closed, it says what it is set to. Open, it holds its controls.", () =>
        [
            new Disclosure { Title = "Sample section", Summary = "sample-model, 2 tools", MinWidth = Tokens.Number("DvTooltipWidth") },
            new Disclosure
            {
                Title = "Sample open section",
                Status = new Badge { Text = "Idle" },
                Open = true,
                Body = Styles.Text("Sample content of an open section."),
                MinWidth = Tokens.Number("DvTooltipWidth"),
            },
        ]),
        new(
            "Timeline",
            "A run with a lane for each bot. Work handed across leaves in violet and its answer comes back the same way. Only the node being worked on moves. One tab stop: arrow keys move, Enter opens.",
            () =>
            [
                FullRow(new Timeline { Model = run, Label = "Sample run", EmptyText = EmptyRun, Detail = SampleDetail }),
                FullRow(new Timeline { Model = run, Label = "Sample run as a list", EmptyText = EmptyRun, AsList = true, Detail = SampleDetail }),
                new Timeline { Model = TimelineModel.Empty, Label = "Sample empty run", EmptyText = EmptyRun },
            ]),
        new("Code block", null, () => [new CodeBlock { Code = Command, CodeLanguage = "bash" }]),
        new(
            "Live states",
            "The four things that move. Each runs only while the work it reports is in progress; here they are held on as samples.",
            LiveStates),
        new("Not found", "What an address with no screen shows.", () => [new NotFound()]),
    ];

    private static string EmptyRun => "Nothing has run in this sample conversation yet.";

    private static TextBlock SampleDetail(TimelineNode node) => Styles.Text(
        $"Sample details for \"{node.Event.Label}\": the arguments, the output as it streams, the result, how long it took, and its cost.");

    private static T FullRow<T>(T element)
        where T : UIElement
    {
        FlowPanel.SetFullRow(element, true);
        return element;
    }

    private static IEnumerable<UIElement> LiveStates()
    {
        var dot = new LiveLight { State = LiveState.Thinking, Round = true, Width = Tokens.Number("DvSpace3"), Height = Tokens.Number("DvSpace3") };

        var arriving = Styles.Text("Text arrives");
        arriving.Loaded += (_, _) => Motion.Arrive(arriving);

        var card = new Grid();
        var cardText = new StackPanel { Spacing = Tokens.Number("DvSpace1"), Padding = new(Tokens.Number("DvSpace3")) };
        cardText.Children.Add(Styles.Text("sample_tool", "DvTextCode"));
        cardText.Children.Add(Styles.Text("Running a tool"));
        var cardBox = new Border { Child = cardText, BorderThickness = new(Tokens.Number("DvBorder")), CornerRadius = new(Tokens.Number("DvRadiusCard")) };
        cardBox.WhenShownOrRethemed(() =>
        {
            cardBox.Background = Tokens.Brush(cardBox, "DvBg2");
            cardBox.BorderBrush = Tokens.Brush(cardBox, "DvLine");
        });
        card.Children.Add(cardBox);
        card.Children.Add(new TravelLine { IsRunning = true });

        var line = new LiveLight { State = LiveState.Passing, Width = Tokens.Number("DvSpace7"), Height = Tokens.Number("DvFocusWidth") };

        return [Row(dot, "Thinking"), arriving, card, Row(line, "Work passes between bots")];
    }

    private static StackPanel Row(FrameworkElement mark, string words)
    {
        mark.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2") };
        row.Children.Add(mark);
        row.Children.Add(Styles.Text(words));
        return row;
    }

    // A sample row of a sample run. Every value is made up, and the page says so at its top.
    private static TimelineEvent Sample(
        int number, Guid bot, TimelineKind kind, string label, TimelineState state, int startsAfter, int? took, string? summary = null, Guid? child = null) =>
        new(
            new Guid(number, 0, 0x7000, 0x80, 0, 0, 0, 0, 0, 0, 0x09),
            bot,
            kind,
            label,
            state,
            sampleStart.AddSeconds(startsAfter),
            took is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            summary,
            child);

    #endregion
}
