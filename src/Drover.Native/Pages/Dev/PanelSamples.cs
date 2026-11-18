namespace Drover.Native.Pages;

/// <summary>
/// The gallery's samples of the panels that read from the server: each is the real panel, with its real view model,
/// asking <see cref="SampleServer"/> instead of a server. Every name, text, and value is a sample.
/// </summary>
internal static class PanelSamples
{
    #region Private Fields

    private const int PictureWidth = 320;

    private const int PictureHeight = 180;

    private static readonly DateTimeOffset sampleStart = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    private static readonly UsageTotals noUsage = new(0, 0, 0, 0);

    private static readonly string space = SampleServer.Space;

    #endregion

    #region Properties

    /// <summary>Where a sample link goes: the gallery itself.</summary>
    public const string Here = "/dev/gallery";

    #endregion

    #region Public Methods

    /// <summary>A file card with a picture, drawn for the gallery and read through the sample server.</summary>
    public static FileCard Picture()
    {
        var id = Id(0x701);
        var server = new SampleServer().AnswerBytes($"{space}/files/{id}", SamplePicture(), "image/png");
        var (api, _) = server.Connect();
        var picture = new SharedFile(id, "sample-chart.png", "image/png", 9_000, api.FileAddress(SampleServer.SpaceId, id), true, sampleStart, Width: PictureWidth, Height: PictureHeight);
        return new(new(Log.For<FileCardViewModel>()), api) { File = picture };
    }

    public static IEnumerable<UIElement> Summaries()
    {
        var server = new SampleServer();
        var (api, session) = server.Connect();

        SummaryPanel Panel(int number, ConversationSummary? summary, bool held = false)
        {
            var (bot, thread) = (Bot(number, "Sample bot"), Id(number + 0x40));
            server.Answer("PUT", $"{space}/bots/{bot.Id}/main-thread", new ThreadSummary(thread, bot.Id, null, false, sampleStart, null, IsMain: true));
            if (held)
            {
                server.Hold("GET", $"{space}/threads/{thread}/summary");
            }
            else if (summary is not null)
            {
                server.Answer("GET", $"{space}/threads/{thread}/summary", summary);
            }

            return Wide(new SummaryPanel(new(api, session, Log.For<SummaryPanelViewModel>())) { Bot = bot });
        }

        return
        [
            Panel(0x801, null, held: true),
            Panel(0x802, null),
            Panel(0x803, new(null, 0, null, ServerWrites: false)),
            Panel(0x804, new(null, 0, null)),
            Panel(0x805, new("A sample summary with **bold** text.\n\n- A sample point\n- Another sample point", 12, sampleStart)),
            Panel(0x806, new("A sample summary that is no longer rewritten.", 1, sampleStart, ServerWrites: false)),
        ];
    }

    public static IEnumerable<UIElement> Screens()
    {
        var server = new SampleServer();
        var (api, session) = server.Connect();

        ScreenPanel Panel(int number, BotSandboxStatus? status, bool held = false, bool refused = false)
        {
            var bot = Bot(number, "Sample bot");
            if (held)
            {
                server.Hold("GET", $"{space}/bots/{bot.Id}/sandbox");
            }
            else if (refused)
            {
                server.Refuse("GET", $"{space}/bots/{bot.Id}/sandbox", HttpStatusCode.Forbidden);
            }
            else if (status is not null)
            {
                server.Answer("GET", $"{space}/bots/{bot.Id}/sandbox", status);
            }

            return Wide(new ScreenPanel(new(api, session, TimeProvider.System, Log.For<ScreenPanelViewModel>()), SampleSurface()) { Bot = bot });
        }

        return
        [
            Panel(0x901, null, held: true),
            Panel(0x902, null),
            Panel(0x907, null, refused: true),
            Panel(0x903, new(Available: false, HasSandbox: false, "none", null, Desktop: false, BotWorking: false, DesktopOffered: false)),
            Panel(0x904, new(Available: true, HasSandbox: true, "running", null, Desktop: false, BotWorking: false, DesktopOffered: false)),
            Panel(0x905, new(Available: true, HasSandbox: false, "none", null, Desktop: false, BotWorking: false, DesktopOffered: true)),
            Panel(0x906, new(Available: true, HasSandbox: true, "running", null, Desktop: true, BotWorking: true, DesktopOffered: true)),
        ];
    }

    public static IEnumerable<UIElement> Activities()
    {
        var server = new SampleServer();
        var (api, session) = server.Connect();
        var (lead, helper) = (Bot(0xa01, "Sample lead bot"), Bot(0xa02, "Sample helper"));

        ActivityPanel Panel(BotSummary bot, Guid thread, Guid? activeRun, bool held = false, bool known = true)
        {
            if (held)
            {
                server.Hold("PUT", $"{space}/bots/{bot.Id}/main-thread");
            }
            else if (known)
            {
                server.Answer("PUT", $"{space}/bots/{bot.Id}/main-thread", new ThreadSummary(thread, bot.Id, null, false, sampleStart, activeRun, IsMain: true));
                server.Answer("GET", $"{space}/threads/{thread}/messages", Array.Empty<MessageResponse>());
            }

            return Wide(new ActivityPanel(new(api, session, Log.For<ActivityPanelViewModel>())) { Bot = bot, Bots = [lead, helper] });
        }

        var run = Id(0xa10);
        server.Stream($"{space}/runs/{run}/events", SampleRun(run, lead.Id, helper.Id));

        return
        [
            Panel(Bot(0xa03, "Sample bot"), Id(0xa43), null, held: true),
            Panel(Bot(0xa04, "Sample bot"), Id(0xa44), null, known: false),
            Panel(Bot(0xa05, "Sample bot"), Id(0xa45), null),
            Panel(lead, Id(0xa41), run),
        ];
    }

    #endregion

    #region Private Methods

    private static Guid Id(int number) => new(number, 0, 0x7000, 0x80, 0, 0, 0, 0, 0, 0, 0x0c);

    private static BotSummary Bot(int number, string name) =>
        new(new(0, 0, 0x7000, 0x80, 0, 0, 0, 0, 0, (byte)(number >> 8), (byte)number), name, "sample", string.Empty, Guid.Empty, "sample-model", "active");

    private static T Wide<T>(T element)
        where T : UIElement
    {
        FlowPanel.SetFullRow(element, true);
        return element;
    }

    // Where the bot's screen will be drawn. In the gallery it is a box that says so.
    private static Border SampleSurface()
    {
        var box = new Border
        {
            Child = Styles.Text("Sample surface: the bot's screen is drawn here.", "DvTextSecondary"),
            Padding = new(Tokens.Number("DvSpace4")),
            BorderThickness = new(Tokens.Number("DvBorder")),
        };
        box.WhenShownOrRethemed(() =>
        {
            box.Background = Tokens.Brush(box, "DvBg2");
            box.BorderBrush = Tokens.Brush(box, "DvLine");
        });
        return box;
    }

    // A sample picture: a surface with one line across it, in token colors, so the gallery needs no picture file.
    private static byte[] SamplePicture()
    {
        var scope = new Grid();
        using var surface = SKSurface.Create(new SKImageInfo(PictureWidth, PictureHeight));
        using var line = new SKPaint { Color = Tokens.Color(scope, "DvIon").ToSkia(), StrokeWidth = (float)Tokens.Number("DvFocusWidth"), IsAntialias = true };
        surface.Canvas.Clear(Tokens.Color(scope, "DvBg3").ToSkia());
        surface.Canvas.DrawLine(0, PictureHeight, PictureWidth, 0, line);
        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        return encoded.ToArray();
    }

    // A sample run that waits for a person: the lead ran a command and handed work to its helper, which asks before
    // it writes a file. Nothing in it is working, so nothing in it moves.
    private static List<RunEventResponse> SampleRun(Guid run, Guid lead, Guid helper)
    {
        var (child, childThread) = (Id(0xa11), Id(0xa12));
        var (firstMessage, secondMessage, childMessage) = (Id(0xa21), Id(0xa22), Id(0xa23));
        var (command, handoff, write) = (Id(0xa31), Id(0xa32), Id(0xa33));
        var task = "Sample task: find three sample sources.";
        List<RunEventResponse> log = [];

        void Add(Guid runId, string type, object payload) =>
            log.Add(new(log.Count + 1, runId, type, RunEventTypes.Write(payload), sampleStart.AddSeconds(log.Count)));

        MessageResponse Message(Guid id, Guid runId, params MessagePartResponse[] parts) => new(id, childThread, null, "assistant", runId, parts, sampleStart);

        Add(run, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 0, null));
        Add(run, RunEventTypes.StepStarted, new StepChanged(1, firstMessage));
        Add(run, RunEventTypes.MessageDelta, new MessageDelta(firstMessage, 0, "text", "A sample line of the lead's work."));
        Add(run, RunEventTypes.ToolRequested, new ToolRequested(command, "sandbox.run", "sandbox.run", """{"command":"sample --command"}""", "allow"));
        Add(run, RunEventTypes.MessageCompleted, new MessageCompleted(Message(
            firstMessage,
            run,
            new MessagePartResponse("text") { Text = "A sample line of the lead's work." },
            new MessagePartResponse("tool_call") { ToolCallId = command, ToolName = "sandbox.run", Arguments = """{"command":"sample --command"}""" })));
        Add(run, RunEventTypes.ToolStarted, new ToolStarted(command));
        Add(run, RunEventTypes.ToolOutput, new ToolOutput(command, "stdout", "sample output\n"));
        Add(run, RunEventTypes.ToolOutput, new ToolOutput(command, "stderr", "sample error output\n"));
        Add(run, RunEventTypes.ToolCompleted, new ToolCompleted(command, "completed", false, """{"exit_code":0,"stdout":"sample output"}""", 1200));
        Add(run, RunEventTypes.StepStarted, new StepChanged(2, secondMessage));
        Add(run, RunEventTypes.ToolRequested, new ToolRequested(handoff, "delegate", "Hand work to a bot", $$"""{"bot_id":"{{helper}}","task":"{{task}}"}""", "allow"));
        Add(run, RunEventTypes.MessageCompleted, new MessageCompleted(Message(
            secondMessage,
            run,
            new MessagePartResponse("tool_call") { ToolCallId = handoff, ToolName = "delegate", Arguments = $$"""{"bot_id":"{{helper}}","task":"{{task}}"}""" })));
        Add(run, RunEventTypes.ToolStarted, new ToolStarted(handoff));
        Add(run, RunEventTypes.DelegationStarted, new DelegationStarted(handoff, child, helper, childThread));
        Add(child, RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 0, null));
        Add(child, RunEventTypes.StepStarted, new StepChanged(1, childMessage));
        Add(child, RunEventTypes.MessageDelta, new MessageDelta(childMessage, 0, "text", "A sample line of the helper's work."));
        Add(child, RunEventTypes.ToolRequested, new ToolRequested(write, "sandbox.write_file", "sandbox.write_file", """{"path":"/sample/notes.md","content":"A sample line."}""", "ask"));
        Add(child, RunEventTypes.ToolApprovalRequested, new ToolApprovalRequested(write, "sample-request", "sample-call"));
        Add(child, RunEventTypes.MessageCompleted, new MessageCompleted(Message(
            childMessage,
            child,
            new MessagePartResponse("text") { Text = "A sample line of the helper's work." },
            new MessagePartResponse("tool_call") { ToolCallId = write, ToolName = "sandbox.write_file", Arguments = """{"path":"/sample/notes.md","content":"A sample line."}""" })));
        Add(child, RunEventTypes.RunWaiting, new RunStatusChanged("waiting", "awaiting_approval", null, 0, noUsage));
        Add(run, RunEventTypes.RunWaiting, new RunStatusChanged("waiting", "awaiting_approval", null, 0, noUsage));
        return log;
    }

    #endregion
}
