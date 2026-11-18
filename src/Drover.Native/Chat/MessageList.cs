namespace Drover.Native.Chat;

/// <summary>
/// What was said in a conversation, newest at the foot. While the reader is at the foot the newest message stays
/// in view as text streams in; once they have scrolled up to read, the list stays where they put it. Sending
/// brings the newest message into view wherever they were, and a link to a moment lands on its message.
/// </summary>
public sealed class MessageList : Grid
{
    #region Private Fields

    private readonly ScrollViewer scroller = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        IsTabStop = true,
    };

    private readonly StackPanel column = new() { Spacing = Tokens.Number("DvSpace5"), VerticalAlignment = VerticalAlignment.Bottom };

    private readonly Grid floor = new();

    private readonly StackPanel start = new() { Spacing = Tokens.Number("DvSpace3"), HorizontalAlignment = HorizontalAlignment.Center };

    private readonly Avatar startHead = new() { Size = 96, HorizontalAlignment = HorizontalAlignment.Center };

    private readonly TextBlock startHint = Styles.Text(null);

    private readonly Grid thinking = new() { ColumnSpacing = Tokens.Number("DvSpace3") };

    private readonly Facet thinkingPlate = new();

    private readonly Avatar thinkingHead = new() { Size = 48, State = AvatarState.Thinking };

    private readonly TextBlock thinkingName = Styles.Text(null, "DvTextMono");

    private readonly TextBlock thinkingState = Styles.Text("Thinking", "DvTextMono");

    private readonly FailureLine failure = new();

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly StatusNote notice = new();

    private readonly SpokenWords spoken = new();

    private MessageListViewModel? model;

    private ChatEntryParameters? parameters;

    private ImmutableHashSet<Guid> forgotten = [];

    private Guid? shownFailure;

    private bool atFoot = true;

    private bool drawQueued;

    #endregion

    #region Constructors

    public MessageList()
    {
        BuildStart();
        BuildThinking();
        failure.Retry += async (_, _) => await (model?.Retry() ?? Task.CompletedTask);

        column.MaxWidth = Tokens.Number("DvMeasure");
        column.Padding = Tokens.Space("DvSpace4", "DvSpace5");
        column.Children.Add(start);
        column.Children.Add(thinking);
        column.Children.Add(failure);
        column.Children.Add(problem);
        column.Children.Add(notice);

        // The column sits at the foot of the view while it is shorter than the view, and grows upward from there.
        floor.Children.Add(column);
        scroller.Content = floor;
        scroller.SizeChanged += (_, _) => floor.MinHeight = scroller.ActualHeight;
        scroller.ViewChanged += (_, _) => atFoot = scroller.VerticalOffset >= scroller.ScrollableHeight - Tokens.Number("DvFocusWidth");
        column.SizeChanged += (_, _) => KeepFoot();

        Children.Add(scroller);
        Children.Add(spoken);
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    /// <summary>What the list shows: set by the conversation it belongs to.</summary>
    public MessageListViewModel? Model
    {
        get => model;
        set
        {
            if (model is not null)
            {
                model.PropertyChanged -= Changed;
                model.NewestRequested -= ShowNewest;
            }

            model = value;

            if (model is not null)
            {
                model.PropertyChanged += Changed;
                model.NewestRequested += ShowNewest;
            }

            Draw();
        }
    }

    /// <summary>How many messages are on the page now, for a bench that measures the list.</summary>
    public int Count => column.Children.OfType<ChatEntry>().Count();

    #endregion

    #region Public Methods

    /// <summary>
    /// Brings the newest message into view. For when the person sends: they want to see what they wrote,
    /// wherever they had scrolled to. Replies do not call this, so reading further up is never interrupted.
    /// </summary>
    public void ShowNewest()
    {
        atFoot = true;
        QueueDraw();
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ToFoot);
    }

    #endregion

    #region Building

    private void BuildStart()
    {
        startHint.TextAlignment = TextAlignment.Center;
        start.Padding = new(0, Tokens.Number("DvSpace7"), 0, Tokens.Number("DvSpace7"));
        start.Children.Add(startHead);
        start.Children.Add(startHint);
    }

    private void BuildThinking()
    {
        thinkingPlate.Cut = Tokens.Number("DvCutS");
        thinkingPlate.Padding = new(Tokens.Number("DvSpace1"));
        thinkingPlate.VerticalAlignment = VerticalAlignment.Top;
        thinkingPlate.Children.Add(thinkingHead);

        var from = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2"), VerticalAlignment = VerticalAlignment.Top };
        from.Padding = new(0, Tokens.Number("DvSpace1"), 0, 0);
        from.Children.Add(thinkingName);
        from.Children.Add(thinkingState);
        AutomationProperties.SetAccessibilityView(thinkingName, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(thinkingState, AccessibilityView.Raw);

        thinking.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        thinking.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        SetColumn(from, 1);
        thinking.Children.Add(thinkingPlate);
        thinking.Children.Add(from);
    }

    #endregion

    #region Drawing

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change) => QueueDraw();

    // A reply can arrive many words a second. The list is drawn once for all that arrived since it was last drawn.
    private void QueueDraw()
    {
        if (!drawQueued)
        {
            drawQueued = true;
            DispatcherQueue.TryEnqueue(Draw);
        }
    }

    private void Draw()
    {
        drawQueued = false;

        if (model is not { } list)
        {
            return;
        }

        AutomationProperties.SetName(scroller, list.Name);
        var with = Parameters(list);

        start.Visibility = Shown(list.IsEmpty);
        startHead.BotName = thinkingHead.BotName = list.BotName;
        startHead.Hue = thinkingHead.Hue = list.BotHue;
        startHint.Text = list.StartHint;

        // The messages stand between the hint that opens an empty conversation and what follows the newest of them.
        Keyed.Sync(
            column,
            list.Messages,
            message => message.Key,
            _ => new ChatEntry(),
            (entry, message) => entry.Show(message, with, list.IsMoment(message)),
            start: 1,
            tail: 4);

        thinking.Visibility = Shown(list.Thinking);
        thinkingName.Text = list.BotName;
        ShowFailure(list);
        problem.Text = list.Problem;
        notice.Text = list.Notice;
        spoken.Say(list.Spoken);
        Paint();

        if (list.TakeMoment() is { } moment)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => ShowMoment(moment));
        }
    }

    // What every message is shown with stays the same object while nothing in it changed, so a message that did
    // not change is not looked at again.
    private ChatEntryParameters Parameters(MessageListViewModel list)
    {
        if (!forgotten.SetEquals(list.ForgottenNotes))
        {
            forgotten = [.. list.ForgottenNotes];
        }

        var now = new ChatEntryParameters(list.BotName, list.BotHue)
        {
            Asker = list.Asker,
            RunAddress = list.RunAddress,
            Decide = list.Decide,
            Forget = list.Forget,
            ForgottenNotes = forgotten,
            MemoryHref = list.MemoryHref,
            Takeovers = list.Takeovers,
            ReadWhole = list.ReadWhole,
            LoadUsage = list.LoadUsage,
        };

        return parameters = now == parameters ? parameters : now;
    }

    // A new failure gets a line of its own, so what a person opened on the last one does not carry over.
    private void ShowFailure(MessageListViewModel list)
    {
        failure.Visibility = Shown(list.Failure is not null);

        if (list.Failure is not { } failed || list.FailureCause is not { } cause)
        {
            shownFailure = null;
            return;
        }

        if (failed.RunId != shownFailure)
        {
            shownFailure = failed.RunId;
            failure.Tag = failed.RunId;
        }

        failure.Cause = cause;
        failure.BotName = list.BotName;
        failure.Reported = failed.Reported;
        failure.Retrying = list.Retrying;
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private void Paint()
    {
        startHint.Foreground = Tokens.Brush(this, "DvText2");
        thinkingPlate.Edge = Tokens.Brush(this, "DvIon");
        thinkingState.Foreground = Tokens.Brush(this, "DvIon");
    }

    #endregion

    #region Scrolling

    // The column grew or shrank. A reader at the foot stays at the foot; one who scrolled up stays where they are.
    private void KeepFoot()
    {
        if (atFoot)
        {
            ToFoot();
        }
    }

    private void ToFoot() => scroller.ChangeView(null, scroller.ScrollableHeight, null, disableAnimation: true);

    // Brings the message a link named into the middle of the view and puts the focus on it, so a keyboard or a
    // screen reader lands there too.
    private void ShowMoment(Guid moment)
    {
        if (column.Children.OfType<ChatEntry>().FirstOrDefault(entry => entry.Tag as string == moment.ToString()) is not { } entry)
        {
            return;
        }

        var top = entry.TransformToVisual(floor).TransformPoint(new(0, 0)).Y;
        var middle = top + (entry.ActualHeight / 2) - (scroller.ViewportHeight / 2);
        atFoot = false;
        scroller.ChangeView(null, Math.Clamp(middle, 0, scroller.ScrollableHeight), null, disableAnimation: true);
        entry.Land();
    }

    #endregion
}
