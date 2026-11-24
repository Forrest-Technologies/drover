namespace Drover.Native.Controls;

/// <summary>
/// Proves focused controls inside <see cref="ActivityPanel"/> keep focus when new events arrive in both lanes and
/// list forms: a button inside an open <see cref="RunMomentDetail"/>, a focused <see cref="TimelineNodeButton"/>,
/// and an expanded list section rendered through <see cref="Keyed"/>. Only compiled when
/// <c>DroverTimelineFocusCheck=true</c> and started with <c>DROVER_TIMELINE_FOCUS_CHECK=1</c>.
/// </summary>
internal static class TimelineFocusCheck
{
    #region Private Fields

    private static readonly Guid spaceId = Guid.Parse("00000000-0000-7000-8000-0000000000f0");

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000010");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-000000000011");

    private static readonly Guid runId = Guid.Parse("0196f1a0-0000-7000-8000-000000000012");

    private static readonly Guid messageId = Guid.Parse("0196f1a0-0000-7000-8000-000000000013");

    private static readonly Guid approvalCallId = Guid.Parse("0196f1a0-0000-7000-8000-000000000014");

    private static readonly ILogger logger = Log.Factory.CreateLogger(nameof(TimelineFocusCheck));

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private static readonly DateTimeOffset epoch = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    private static readonly UsageTotals noUsage = new(0, 0, 0, 0);

    private static string stage = "startup";

    #endregion

    #region Public Methods

    /// <summary>
    /// Runs the focus preservation checks across lanes form and list form on <see cref="ActivityPanel"/>.
    /// </summary>
    public static void Start(Window window)
    {
        Volatile.Write(ref stage, "startup");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            Fail($"The activity focus check did not finish within 30 seconds (stage: {Volatile.Read(ref stage)}).");
        });

        var server = new LiveRunServer();
        var (api, session) = server.Connect();
        var bot = new BotSummary(botId, "Sample", "sample", string.Empty, Guid.Empty, "sample-model", "active");
        var panel = new ActivityPanel(new(api, session, Log.For<ActivityPanelViewModel>()))
        {
            Bot = bot,
            Bots = [bot],
        };

        panel.Loaded += (_, _) => _ = RunChecks(panel, server);
        window.Content = panel;
    }

    #endregion

    #region Private Methods

    private static async Task RunChecks(ActivityPanel panel, LiveRunServer server)
    {
        try
        {
            List<string> failures = [];
            var seq = 0;
            void Send(string type, object payload) =>
                server.Push(new(++seq, runId, type, RunEventTypes.Write(payload), epoch.AddSeconds(seq)));

            Send(RunEventTypes.RunStarted, new RunStatusChanged("running", null, null, 0, null));
            Send(RunEventTypes.StepStarted, new StepChanged(1, messageId));
            Send(RunEventTypes.MessageDelta, new MessageDelta(messageId, 0, "text", "Sample step."));
            Send(RunEventTypes.ToolRequested, new ToolRequested(
                approvalCallId, "sandbox.write_file", "sandbox.write_file",
                """{"path":"/sample/notes.md","content":"hello"}""", "ask"));
            Send(RunEventTypes.ToolApprovalRequested, new ToolApprovalRequested(approvalCallId, "req-1", "call-1"));
            Send(RunEventTypes.RunWaiting, new RunStatusChanged("waiting", "awaiting_approval", null, 0, noUsage));

            // 1. Lanes form: open the approval node inside ActivityPanel and focus Approve inside RunMomentDetail.
            Volatile.Write(ref stage, "case 1: waiting for lanes approval node");
            var approvalNode = await WaitFor(() => FindNodes(panel).FirstOrDefault(node => node.Node.Event.Kind is TimelineKind.Waiting));
            var approvalEventId = approvalNode.Node.Event.Id;
            var countBeforeCase1 = FindNodes(panel).Count;
            Invoke(approvalNode);

            Volatile.Write(ref stage, "case 1: focusing Approve inside RunMomentDetail (lanes form)");
            var approveInLanes = await WaitFor(() => FindButton(panel, RunMomentDetailViewModel.Approve));
            await FocusControl(panel, approveInLanes, "Approve inside RunMomentDetail (lanes form)");

            Send(RunEventTypes.ToolRequested, new ToolRequested(
                Guid.Parse("0196f1a0-0000-7000-8000-000000000021"), "sandbox.run", "sandbox.run",
                """{"command":"ls"}""", "allow"));

            Volatile.Write(ref stage, "case 1: waiting for lanes timeline update");
            await WaitFor(() => FindNodes(panel).Count > countBeforeCase1 ? true : null);
            await Settle(panel);

            var afterLanesDetail = FindButton(panel, RunMomentDetailViewModel.Approve);
            if (!ReferenceEquals(approveInLanes, afterLanesDetail) || approveInLanes.FocusState is FocusState.Unfocused)
            {
                failures.Add("Case 1: Focus left RunMomentDetail in lanes form when a new event arrived.");
            }

            // 2. Lanes form: focus a TimelineNodeButton itself and send a new event.
            Volatile.Write(ref stage, "case 2: focusing TimelineNodeButton (lanes form)");
            var focusedNode = FindNodes(panel).First(node => node.Node.Event.Id == approvalEventId);
            var countBeforeCase2 = FindNodes(panel).Count;
            await FocusControl(panel, focusedNode, "TimelineNodeButton (lanes form)");

            Send(RunEventTypes.ToolRequested, new ToolRequested(
                Guid.Parse("0196f1a0-0000-7000-8000-000000000022"), "sandbox.run", "sandbox.run",
                """{"command":"pwd"}""", "allow"));

            Volatile.Write(ref stage, "case 2: waiting for lanes timeline update");
            await WaitFor(() => FindNodes(panel).Count > countBeforeCase2 ? true : null);
            await Settle(panel);

            var afterNode = FindNodes(panel).FirstOrDefault(node => node.Node.Event.Id == approvalEventId);
            if (!ReferenceEquals(focusedNode, afterNode) || focusedNode.FocusState is FocusState.Unfocused)
            {
                failures.Add("Case 2: Focus left TimelineNodeButton in lanes form when a new event arrived.");
            }

            // 3. List form (Keyed.Sync): switch ActivityPanel to List, open approval Disclosure, focus Approve inside RunMomentDetail.
            Volatile.Write(ref stage, "case 3: switching to list form");
            var form = await WaitFor(() => First<SegmentedControl>(panel));
            form.Value = "list";

            var sections = await WaitFor(() =>
            {
                var found = All<Disclosure>(panel);
                return found.Count >= countBeforeCase2 + 1 ? found : null;
            });
            var countBeforeCase3 = sections.Count;
            var approvalSection = sections.First(section => section.Title.Contains("Asked to", StringComparison.Ordinal));
            approvalSection.Open = true;

            Volatile.Write(ref stage, "case 3: focusing Approve inside RunMomentDetail (list form)");
            var approveInList = await WaitFor(() => FindButton(panel, RunMomentDetailViewModel.Approve));
            await FocusControl(panel, approveInList, "Approve inside RunMomentDetail (list form)");

            Send(RunEventTypes.ToolRequested, new ToolRequested(
                Guid.Parse("0196f1a0-0000-7000-8000-000000000023"), "sandbox.run", "sandbox.run",
                """{"command":"id"}""", "allow"));

            Volatile.Write(ref stage, "case 3: waiting for list update");
            await WaitFor(() => All<Disclosure>(panel).Count > countBeforeCase3 ? true : null);
            await Settle(panel);

            var afterListDetail = FindButton(panel, RunMomentDetailViewModel.Approve);
            if (!ReferenceEquals(approveInList, afterListDetail) || approveInList.FocusState is FocusState.Unfocused)
            {
                failures.Add("Case 3: Focus left RunMomentDetail in list form (Keyed.Sync) when a new event arrived.");
            }

            // 4. List form (Keyed.Sync): focus the Disclosure toggle button and send a new event.
            Volatile.Write(ref stage, "case 4: focusing Disclosure toggle (list form)");
            var currentSections = All<Disclosure>(panel);
            var countBeforeCase4 = currentSections.Count;
            var currentSection = currentSections.First(section => section.Title.Contains("Asked to", StringComparison.Ordinal));
            var toggle = First<PlainButton>(currentSection) ?? throw new InvalidOperationException("Disclosure toggle missing.");
            await FocusControl(panel, toggle, "Disclosure toggle (list form)");

            Send(RunEventTypes.ToolRequested, new ToolRequested(
                Guid.Parse("0196f1a0-0000-7000-8000-000000000024"), "sandbox.run", "sandbox.run",
                """{"command":"date"}""", "allow"));

            Volatile.Write(ref stage, "case 4: waiting for list update");
            await WaitFor(() => All<Disclosure>(panel).Count > countBeforeCase4 ? true : null);
            await Settle(panel);

            var afterSection = All<Disclosure>(panel).FirstOrDefault(section => section.Title.Contains("Asked to", StringComparison.Ordinal));
            var afterToggle = afterSection is null ? null : First<PlainButton>(afterSection);
            if (!ReferenceEquals(currentSection, afterSection) || !ReferenceEquals(toggle, afterToggle) || toggle.FocusState is FocusState.Unfocused)
            {
                failures.Add("Case 4: Focus left the open list section (Keyed.Sync) when a new event arrived.");
            }

            if (failures.Count > 0)
            {
                Fail(string.Join(" | ", failures));
                return;
            }

            logger.LogInformation("Activity focus check passed.");
            Exit(0);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The activity focus check failed.");
            Exit(1);
        }
    }

    private static async Task FocusControl(ActivityPanel panel, Control control, string name)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (control.IsLoaded && control.Focus(FocusState.Programmatic) && control.FocusState is not FocusState.Unfocused)
            {
                return;
            }

            await Task.Delay(25);
            await Settle(panel);
        }

        Fail($"{name} did not take focus.");
    }

    private static async Task<T> WaitFor<T>(Func<T?> probe)
        where T : class
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (probe() is { } value)
            {
                return value;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"Timed out waiting for {typeof(T).Name}.");
    }

    private static async Task<bool> WaitFor(Func<bool?> probe)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (probe() is true)
            {
                return true;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("Timed out waiting for timeline update.");
    }

    private static Task Settle(UIElement root)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => done.TrySetResult());
        return done.Task;
    }

    private static void Invoke(Microsoft.UI.Xaml.Controls.Button button)
    {
        if (new ButtonAutomationPeer(button) is Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoke)
        {
            invoke.Invoke();
            return;
        }

        throw new InvalidOperationException("Button does not expose IInvokeProvider.");
    }

    private static List<TimelineNodeButton> FindNodes(DependencyObject root) => All<TimelineNodeButton>(root);

    private static Button? FindButton(DependencyObject root, string label) =>
        All<Button>(root).FirstOrDefault(button => string.Equals(button.Content as string, label, StringComparison.Ordinal));

    private static T? First<T>(DependencyObject root)
        where T : DependencyObject =>
        All<T>(root).FirstOrDefault();

    private static List<T> All<T>(DependencyObject root)
        where T : DependencyObject
    {
        List<T> found = [];
        Collect(root, found);
        return found;
    }

    private static void Collect<T>(DependencyObject root, List<T> found)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                found.Add(match);
            }

            Collect(child, found);
        }
    }

    private static void Fail(string reason)
    {
        logger.LogError("Activity focus check failed: {Reason}", reason);
        Exit(1);
    }

    private static void Exit(int code)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsAndroid())
        {
            ExitImmediately(code);
        }

        Environment.Exit(code);
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "_exit")]
    private static extern void ExitImmediately(int code);

    #endregion

    #region Nested Types

    private sealed class LiveRunServer : HttpMessageHandler
    {
        #region Private Fields

        private readonly System.Threading.Channels.Channel<byte[]> chunks = System.Threading.Channels.Channel.CreateUnbounded<byte[]>();

        #endregion

        #region Public Methods

        public (DroverApi Api, SessionState Session) Connect()
        {
#pragma warning disable RS0030
            var api = new DroverApi(new HttpClient(this, disposeHandler: false) { BaseAddress = new("https://sample.invalid/") });
#pragma warning restore RS0030
            var session = new SessionState(api, new ShareInbox());
            session.Begin(new(
                Guid.Parse("00000000-0000-7000-8000-0000000000f1"),
                "sample@example.test",
                "Sample",
                [new(spaceId, "personal", "Sample space", "sample", "owner")]));
            return (api, session);
        }

        public void Push(RunEventResponse happening) =>
            chunks.Writer.TryWrite(System.Text.Encoding.UTF8.GetBytes($"data: {JsonSerializer.Serialize(happening, json)}\n\n"));

        #endregion

        #region Protected Methods

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Put && path.EndsWith($"/bots/{botId}/main-thread", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(new ThreadSummary(threadId, botId, null, false, epoch, runId, IsMain: true)));
            }

            if (request.Method == HttpMethod.Get && path.EndsWith($"/threads/{threadId}/messages", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(Array.Empty<MessageResponse>()));
            }

            if (request.Method == HttpMethod.Get && path.EndsWith($"/runs/{runId}/events", StringComparison.Ordinal))
            {
                var content = new StreamContent(new LiveEventStream(chunks.Reader));
                content.Headers.ContentType = new("text/event-stream");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        #endregion

        #region Private Methods

        private static HttpResponseMessage Json<T>(T body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, json), System.Text.Encoding.UTF8, "application/json"),
            };

        #endregion
    }

    private sealed class LiveEventStream(System.Threading.Channels.ChannelReader<byte[]> reader) : Stream
    {
        #region Private Fields

        private ReadOnlyMemory<byte> current;

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        #endregion

        #region Public Methods

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (current.IsEmpty)
            {
                if (!await reader.WaitToReadAsync(cancellationToken))
                {
                    return 0;
                }

                if (reader.TryRead(out var next))
                {
                    current = next;
                }
            }

            var take = Math.Min(buffer.Length, current.Length);
            current[..take].CopyTo(buffer);
            current = current[take..];
            return take;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    #endregion
}
