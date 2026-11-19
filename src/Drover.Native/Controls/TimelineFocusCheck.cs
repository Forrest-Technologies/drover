namespace Drover.Native.Controls;

/// <summary>
/// Proves a focused control inside an open activity row keeps focus when a new event is drawn. The desktop workflow
/// starts the app with <c>DROVER_TIMELINE_FOCUS_CHECK=1</c>. Any other start never calls this.
/// </summary>
internal static class TimelineFocusCheck
{
    #region Private Fields

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-000000000010");

    private static readonly Guid firstId = Guid.Parse("0196f1a0-0000-7000-8000-000000000011");

    private static readonly Guid secondId = Guid.Parse("0196f1a0-0000-7000-8000-000000000012");

    private static readonly ILogger logger = Log.Factory.CreateLogger(nameof(TimelineFocusCheck));

    private static readonly TextBox editor = new() { Text = "Notes from the run." };

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows one open row, focuses the notes inside it, then adds an event and checks the focus stayed.
    /// </summary>
    public static void Start(Window window)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            Fail("The activity row was not shown within 20 seconds.");
        });

        var timeline = new Timeline { Detail = Detail };
        timeline.Loaded += (_, _) => timeline.DispatcherQueue.TryEnqueue(() => Prove(timeline));
        window.Content = timeline;
    }

    #endregion

    #region Private Methods

    private static TextBox? Detail(TimelineNode node) => node.Event.Id == firstId ? editor : null;

    private static void Prove(Timeline timeline)
    {
        try
        {
            IReadOnlyList<TimelineBot> bots = [new(botId, "Sample")];
            timeline.Model = Timelines.Build(bots, [Event(firstId, "First")]);
            timeline.OpenForCheck(firstId);
            if (timeline.DetailForCheck(firstId) is not TextBox notes)
            {
                Fail("The notes inside the open activity row were not shown.");
                return;
            }

            // A control just drawn cannot take focus until the next pass, the same wait the timeline uses.
            if (notes.IsLoaded)
            {
                notes.DispatcherQueue.TryEnqueue(() => Check(timeline, notes));
            }
            else
            {
                notes.Loaded += (_, _) => notes.DispatcherQueue.TryEnqueue(() => Check(timeline, notes));
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The activity focus check failed.");
            Environment.Exit(1);
        }
    }

    private static void Check(Timeline timeline, TextBox notes)
    {
        try
        {
            if (!notes.Focus(FocusState.Programmatic))
            {
                Fail("The notes inside the open activity row did not take focus.");
                return;
            }

            IReadOnlyList<TimelineBot> bots = [new(botId, "Sample")];
            timeline.Model = Timelines.Build(bots, [Event(firstId, "First"), Event(secondId, "Second")]);
            var kept = timeline.DetailForCheck(firstId);
            if (!ReferenceEquals(notes, kept) || notes.FocusState is FocusState.Unfocused)
            {
                Fail("Focus left the open activity row when a new event arrived.");
                return;
            }

            logger.LogInformation("Activity focus check passed.");
            Environment.Exit(0);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The activity focus check failed.");
            Environment.Exit(1);
        }
    }

    private static TimelineEvent Event(Guid id, string label) =>
        new(id, botId, TimelineKind.Step, label, TimelineState.Done, DateTimeOffset.UnixEpoch);

    private static void Fail(string reason)
    {
        logger.LogError("Activity focus check failed: {Reason}", reason);
        Environment.Exit(1);
    }

    #endregion
}
