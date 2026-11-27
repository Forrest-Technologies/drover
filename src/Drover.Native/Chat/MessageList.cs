namespace Drover.Native.Chat;

/// <summary>
/// What was said in a conversation, newest at the foot. While the reader is at the foot the newest message stays
/// in view as text streams in; once they have scrolled up to read, the list stays where they put it. Sending
/// brings the newest message into view wherever they were, and a link to a moment lands on its message.
/// </summary>
public sealed class MessageList : Grid
{
    #region Private Fields

    private const double FramesInAFade = 4;

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

    // The two ends of the page while the conversation goes on past them. Scrolling toward an end shows more of it;
    // these are the same steps for a keyboard and a screen reader.
    private readonly Button earlier = new() { Content = MessageListViewModel.EarlierLabel, HorizontalAlignment = HorizontalAlignment.Center };

    private readonly Button newest = new() { Content = MessageListViewModel.NewestLabel, HorizontalAlignment = HorizontalAlignment.Center };

    private readonly Grid thinking = new() { ColumnSpacing = Tokens.Number("DvSpace3") };

    private readonly Facet thinkingPlate = new();

    private readonly Avatar thinkingHead = new() { Size = 48, State = AvatarState.Thinking };

    private readonly TextBlock thinkingName = Styles.Text(null, "DvTextMono");

    private readonly TextBlock thinkingState = Styles.Text(TurnLedger.Thinking, "DvTextMono");

    private readonly StackPanel thinkingFrom = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = Tokens.Number("DvSpace2"),
    };

    private ConversationStyle? thinkingStyle;

    // The older reply whose actions a press showed. A press on another hides them: one at a time.
    private ChatEntry? pinned;

    private readonly FailureLine failure = new();

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly StatusNote notice = new();

    private readonly SpokenWords spoken = new();

    private MessageListViewModel? model;

    private ChatEntryParameters? parameters;

    private ImmutableHashSet<Guid> forgotten = [];

    private Guid? shownFailure;

    private bool atFoot = true;

    private bool extendQueued;

    private bool drawQueued;

    private readonly System.Diagnostics.Stopwatch drawn = System.Diagnostics.Stopwatch.StartNew();

    #endregion

    #region Constructors

    public MessageList()
    {
        AutomationProperties.SetAutomationId(scroller, "MessageList");
        BuildStart();
        BuildThinking();
        failure.Retry += async (_, _) => await (model?.Retry() ?? Task.CompletedTask);

        column.MaxWidth = Tokens.Number("DvMeasure");
        column.Padding = Tokens.Space("DvSpace4", "DvSpace5");
        AutomationProperties.SetAutomationId(earlier, "MessageListEarlier");
        AutomationProperties.SetAutomationId(newest, "MessageListNewest");
        earlier.Click += (_, _) => ShowEarlier();
        newest.Click += (_, _) => ShowNewestFromAction();
        column.Children.Add(start);
        column.Children.Add(earlier);
        column.Children.Add(newest);
        column.Children.Add(thinking);
        column.Children.Add(failure);
        column.Children.Add(problem);
        column.Children.Add(notice);

        // The column sits at the foot of the view while it is shorter than the view, and grows upward from there.
        floor.Children.Add(column);
        scroller.Content = floor;
        scroller.SizeChanged += (_, _) =>
        {
            floor.MinHeight = scroller.ActualHeight;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, KeepFoot);
        };
        scroller.ViewChanged += (_, _) =>
        {
            atFoot = model is not { Later: > 0 } && scroller.VerticalOffset >= scroller.ScrollableHeight - Tokens.Number("DvFocusWidth");
            Extend();
        };
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

    /// <summary>Draws what the list has now, without waiting for its next turn. For when it is about to be shown.</summary>
    public void DrawNow() => Draw();

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
        thinkingPlate.VerticalAlignment = VerticalAlignment.Top;
        thinkingFrom.Children.Add(thinkingName);
        thinkingFrom.Children.Add(thinkingState);
        thinkingName.VerticalAlignment = thinkingState.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(thinkingName, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(thinkingState, AccessibilityView.Raw);
        thinking.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        thinking.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        LayThinking();
    }

    // The bot thinking before any of its words: its head on a plate with the words beside it where a conversation is
    // set as a document, and one small pill holding the head and the words where both sides speak in bubbles.
    private void LayThinking()
    {
        var style = ThemeTokens.Current.Surfaces.Conversation;
        if (style == thinkingStyle)
        {
            return;
        }

        thinkingStyle = style;
        var bubbles = style is ConversationStyle.Bubbles;
        thinking.Children.Clear();
        thinkingPlate.Children.Remove(thinkingHead);
        thinkingPlate.Children.Remove(thinkingFrom);
        thinkingPlate.Role = FacetRole.Control;
        thinkingHead.Size = (int)Tokens.Number(bubbles ? "DvAvatar24" : "DvAvatar48");
        thinkingHead.VerticalAlignment = VerticalAlignment.Center;
        thinkingPlate.Children.Add(thinkingHead);

        if (bubbles)
        {
            thinkingPlate.Padding = Tokens.Space("DvSpace3", "DvSpace1");
            thinkingPlate.ColumnSpacing = Tokens.Number("DvSpace2");
            thinkingPlate.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            thinkingPlate.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            thinkingFrom.Padding = new(0);
            thinkingFrom.VerticalAlignment = VerticalAlignment.Center;
            SetColumn(thinkingFrom, 1);
            thinkingPlate.Children.Add(thinkingFrom);
            thinking.Children.Add(thinkingPlate);
            return;
        }

        thinkingPlate.Padding = new(Tokens.Number("DvSpace1"));
        thinkingPlate.ColumnSpacing = 0;
        thinkingPlate.ColumnDefinitions.Clear();
        thinkingFrom.Padding = new(0, Tokens.Number("DvSpace1"), 0, 0);
        thinkingFrom.VerticalAlignment = VerticalAlignment.Top;
        SetColumn(thinkingFrom, 1);
        thinking.Children.Add(thinkingPlate);
        thinking.Children.Add(thinkingFrom);
    }

    // A press showed an older reply's actions: the ones a press showed on another reply go.
    private void WhenPinned(ChatEntry entry)
    {
        if (!ReferenceEquals(pinned, entry))
        {
            pinned?.Unpin();
            pinned = entry;
        }
    }

    #endregion

    #region Drawing

    private void Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs change) => QueueDraw();

    // A reply can arrive many words a second, and a reply picked up again arrives all at once. The list is drawn
    // once for all that arrived since it was last drawn, after what the person is doing, and never so soon after
    // the last time that the screen has no moment to show it.
    private async void QueueDraw()
    {
        if (drawQueued)
        {
            return;
        }

        drawQueued = true;
        var rest = TimeSpan.FromMilliseconds(Tokens.Number("DvDur1") / FramesInAFade) - drawn.Elapsed;
        if (rest > TimeSpan.Zero)
        {
            await Task.Delay(rest);
        }

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Draw);
    }

    private void Draw()
    {
        drawQueued = false;
        drawn.Restart();

        if (model is not { } list)
        {
            return;
        }

        // A link to a moment moves the page to its message first, so the message is there to land on.
        var moment = list.TakeMoment();
        var shown = list.Shown;
        var held = moment is null ? Hold(shown) : null;
        AutomationProperties.SetName(scroller, list.Name);
        var with = Parameters(list);

        start.Visibility = Shown(list.IsEmpty);
        startHead.BotName = thinkingHead.BotName = list.BotName;
        startHead.Hue = thinkingHead.Hue = list.BotHue;
        startHint.Text = list.StartHint;

        // The messages stand between the hint that opens an empty conversation and what follows the newest of them.
        Keyed.Sync(
            column,
            shown,
            message => message.Key,
            _ => NewEntry(),
            (entry, message) => entry.Show(message, with, list.IsMoment(message), list.IsNewestReply(message)),
            start: 2,
            tail: 5);

        earlier.Visibility = Shown(list.Earlier > 0);
        newest.Visibility = Shown(list.Later > 0);

        LayThinking();
        thinking.Visibility = Shown(list.ShowsThinking);
        thinkingName.Text = list.BotName;
        ShowFailure(list);
        // A note with nothing to say takes no room between the messages and the foot of the column. How the
        // conversation ended is said after its newest message, so not while the page ends before it.
        var ends = list.Later == 0;
        failure.Visibility = Shown(ends && list.Failure is not null);
        problem.Text = list.Problem;
        problem.Visibility = Shown(ends && !string.IsNullOrWhiteSpace(list.Problem));
        notice.Text = list.Notice;
        notice.Visibility = Shown(ends && !string.IsNullOrWhiteSpace(list.Notice));
        spoken.Say(list.Spoken);
        Paint();
        held?.Invoke();

        if (moment is { } landed)
        {
            // Layout grows right after this draw, so leave the foot now before SizeChanged pulls the list back down.
            atFoot = false;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => ShowMoment(landed));
        }

        QueueExtend();
    }

    private ChatEntry NewEntry()
    {
        var entry = new ChatEntry();
        entry.Pinned += WhenPinned;
        return entry;
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
            ShowVersion = list.OnShowVersion is null ? null : list.ShowVersion,
            Regenerate = list.OnRegenerate is null ? null : list.Regenerate,
            Edit = list.OnEdit is null ? null : list.Edit,
        };

        return parameters = now == parameters ? parameters : now;
    }

    // A new failure gets a line of its own, so what a person opened on the last one does not carry over.
    private void ShowFailure(MessageListViewModel list)
    {
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
        failure.Limit = failed.Limit;
        failure.LimitsHref = list.FailureLimitsHref;
        failure.Reported = failed.Reported;
        failure.Retrying = list.Retrying;
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private void Paint()
    {
        startHint.Foreground = Tokens.Brush(this, "DvText2");
        var bubbles = thinkingStyle is ConversationStyle.Bubbles;
        thinkingPlate.Fill = bubbles ? Tokens.Brush(this, "DvBg2") : null;
        thinkingPlate.Edge = bubbles ? thinkingPlate.Fill : Tokens.Brush(this, "DvIon");
        thinkingState.Foreground = Tokens.Brush(this, "DvIon");
    }

    #endregion

    #region Scrolling

    // The reader came near an end of the page while the conversation goes on past it: a step more of it is shown,
    // before they reach the end. One view of messages is kept ready each way. Not while the keyboard is on the
    // action at that end: the person there takes each step themselves, and the action stays where they are.
    private void Extend()
    {
        if (model is not { } list || scroller.ViewportHeight <= 0)
        {
            return;
        }

        if (list.Earlier > 0 && scroller.VerticalOffset < scroller.ViewportHeight && !HasFocus(earlier))
        {
            list.ShowEarlier();
        }
        else if (list.Later > 0 && scroller.VerticalOffset > scroller.ScrollableHeight - scroller.ViewportHeight && !HasFocus(newest))
        {
            list.ShowLater();
        }
    }

    private static bool HasFocus(Button action) => action.FocusState is not FocusState.Unfocused;

    // After a draw is laid out, the page may still end too near the reader: one step did not fill a view.
    private void QueueExtend()
    {
        if (extendQueued || model is not { } list || (list.Earlier == 0 && list.Later == 0))
        {
            return;
        }

        extendQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            extendQueued = false;
            Extend();
        });
    }

    private void ShowEarlier()
    {
        model?.ShowEarlier();

        // The action goes when nothing comes before the page; the focus it held stays in the list.
        if (model is { Earlier: 0 } && HasFocus(earlier))
        {
            scroller.Focus(FocusState.Programmatic);
        }
    }

    private void ShowNewestFromAction()
    {
        var focused = HasFocus(newest);
        model?.ShowNewest();

        if (focused)
        {
            scroller.Focus(FocusState.Programmatic);
        }
    }

    // What the reader is looking at is held where it is on the screen while the list is drawn: a page that grows
    // above them, a question that becomes a card, or the events that follow an answer all move what comes after.
    // The work is handed back for after the draw, so no frame shows the reader moved. Nothing is held while they
    // follow the foot, or when no message of the page is in view. A person who asked for earlier messages from the
    // action above the page reads on from that action, so it is what is held.
    private Action? Hold(IReadOnlyList<ChatMessage> shown)
    {
        if (atFoot || shown.Count == 0)
        {
            return null;
        }

        HashSet<string> stays = [.. shown.Select(message => message.Key)];
        var view = scroller.VerticalOffset;
        FrameworkElement? reading = HasFocus(earlier)
            ? earlier
            : column.Children.OfType<ChatEntry>().FirstOrDefault(entry =>
                entry.Tag is string name && stays.Contains(name) && Top(entry) + entry.ActualHeight > view);

        if (reading is null)
        {
            return null;
        }

        var before = Top(reading) - view;
        return () =>
        {
            column.UpdateLayout();
            if (reading.Parent is not null)
            {
                var offset = Top(reading) - before;
                if (offset - scroller.VerticalOffset is > 0.5 or < -0.5)
                {
                    scroller.ChangeView(null, offset, null, disableAnimation: true);
                }
            }
        };
    }

    private double Top(FrameworkElement entry) => entry.TransformToVisual(floor).TransformPoint(new(0, 0)).Y;

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

        atFoot = false;
        var offset = MomentViewport.Offset(Top(entry), entry.ActualHeight, scroller.ViewportHeight, scroller.ScrollableHeight);
        scroller.ChangeView(null, offset, null, disableAnimation: true);
        entry.Land();
    }

    #endregion
}
