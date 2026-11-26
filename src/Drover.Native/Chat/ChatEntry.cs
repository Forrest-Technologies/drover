namespace Drover.Native.Chat;

/// <summary>What a message is shown with: the bot it belongs to, and what its cards may ask of the conversation.</summary>
/// <param name="BotName">The bot whose conversation, or whose part of handed work, the message is in.</param>
/// <param name="BotHue">That bot's identity hue.</param>
public sealed record ChatEntryParameters(string BotName, int BotHue)
{
    #region Properties

    /// <summary>The bot that wrote what a person would have written here, on a bot's side of work handed to it.</summary>
    public string? Asker { get; init; }

    /// <summary>True inside work handed to another bot: no head beside each message, the card has it.</summary>
    public bool Compact { get; init; }

    /// <summary>Where a reply leads to the run that wrote it, as a timeline. Without it, replies lead nowhere.</summary>
    public Func<Guid, string>? RunAddress { get; init; }

    /// <summary>Sends a person's answer to a tool call that waits for one. Returns why it failed, or nothing.</summary>
    public Func<Guid, bool, Task<string?>>? Decide { get; init; }

    /// <summary>Removes a note the bot saved, from its card, while it still says the given text.</summary>
    public Func<Guid, string, Task<ForgetOutcome>>? Forget { get; init; }

    /// <summary>The notes forgotten from their cards, which then say so.</summary>
    public IReadOnlySet<Guid> ForgottenNotes { get; init; } = ImmutableHashSet<Guid>.Empty;

    /// <summary>The bot's memory beside the conversation, where a note that changed since its card can be seen.</summary>
    public string? MemoryHref { get; init; }

    /// <summary>What a request for a person at the bot's screen needs here. Without it such a request is a plain tool card.</summary>
    public ScreenTakeovers? Takeovers { get; init; }

    /// <summary>Reads a text file whole, for a file card that shows only its first lines.</summary>
    public Func<SharedFile, Task<FileText>>? ReadWhole { get; init; }

    /// <summary>Reads what a finished reply's run used. Without it, the reply shows no usage.</summary>
    public Func<Guid, Task<RunUsageDetail>>? LoadUsage { get; init; }

    /// <summary>Shows another version of a place in the conversation. Without it, no message switches versions.</summary>
    public Func<Guid, Task>? ShowVersion { get; init; }

    /// <summary>Asks for another reply, by the id of the reply. Without it, no reply offers one.</summary>
    public Func<Guid, Task>? Regenerate { get; init; }

    /// <summary>Sends an edit of a person's message, by its id. Without it, no message offers an edit.</summary>
    public Func<Guid, string, Task>? Edit { get; init; }

    #endregion
}

/// <summary>
/// One message of a conversation: a person's words and files, or a bot's words and the tool calls it made with
/// them. A call that handed work to another bot is that work's card, with the other bot's own messages inside it,
/// shown by this same control. A bot speaks from its head. Where a theme sets a conversation as a document, the
/// character stands on a plate beside its words, and the plate's edge takes the live color while the bot is writing,
/// and only then. Where a theme sets it as bubbles, the head stands small before the bot's name over the first
/// bubble of its turn, and both sides speak in bubbles with no line around them. A reply's actions (its footer and
/// the way to what happened) stay in view on the newest reply. An older reply shows them while the keyboard's focus
/// is in it, or after a press on its words or its name, and is one stop for the keyboard; hovering does nothing.
/// </summary>
public sealed class ChatEntry : ContentControl
{
    #region Private Fields

    private const double BubbleShare = 0.85;

    private readonly Border ring = new();

    private readonly Facet plate = new();

    private readonly Avatar head = new() { Size = 48 };

    private readonly TextBlock name = Styles.Text(null, "DvTextMono");

    private readonly TextBlock writing = Styles.Text("Writing", "DvTextMono");

    private readonly Link run = new() { Text = "See what happened", MinHeight = Targets.Minimum };

    private readonly MarkdownView words = new();

    private readonly UnusableToolRequestCard unusableReply = new();

    private readonly ReplyReasoning reasoning = new();

    // The turn's work as one list, on the turn's first message.
    private readonly WorkLedger ledger = new();

    private readonly WorkLedger ledgerAfter = new();

    private readonly ReplySources sources = new();

    private readonly StackPanel tools = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly StackPanel files = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly Facet bubble = new();

    // A bot's words, where the theme sets both sides of a conversation in bubbles.
    private readonly Facet reply = new();

    private readonly TextBlock said = Styles.Text(null);

    private readonly TextBlock sentBy = Styles.Text(null, "DvTextCaption");

    private readonly TextBlock pending = Styles.Text(null, "DvTextCaption");

    private readonly ReplyFooter footer = new();

    private readonly MessageVersions versions = new();

    private readonly StackPanel editor = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly TextArea edit = new() { Rows = 3 };

    private readonly Button sendEdit = new() { Kind = ButtonKind.Primary, Content = ChatRows.SendEditLabel };

    private readonly Button cancelEdit = new() { Kind = ButtonKind.Quiet, Content = ChatRows.CancelEditLabel };

    private bool editing;

    private ChatEntryShape? shape;

    private ChatMessage? shown;

    private ChatEntryParameters? with;

    private Guid? usageRun;

    private bool moment;

    private ConversationStyle? laidStyle;

    // Who speaks, over a bot's words: a press on it shows an older reply's actions, as a press on the words does.
    private FrameworkElement? fromRow;

    private bool newest = true;

    private bool hasFooter;

    private bool leads;

    private bool reveals;

    private bool pinned;

    private bool focusWithin;

    // A press on a link in the words and the press the list hears of it come in either order. Each is remembered for
    // the turn it came in, so the link opens and the actions stay as they were.
    private bool linkPressed;

    private bool pressToggled;

    #endregion

    #region Constructors

    public ChatEntry()
    {
        IsTabStop = false;
        UseSystemFocusVisuals = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        // The ring of the message a link landed on lies around the message and takes no room from it.
        var around = Tokens.Number("DvFocusWidth") + Tokens.Number("DvFocusOffset");
        ring.BorderThickness = new(Tokens.Number("DvFocusWidth"));
        ring.Padding = new(Tokens.Number("DvFocusOffset"));
        ring.Margin = new(-around);
        Content = ring;

        plate.Role = FacetRole.Control;
        plate.Padding = new(Tokens.Number("DvSpace1"));
        plate.VerticalAlignment = VerticalAlignment.Top;
        plate.Children.Add(head);

        said.IsTextSelectionEnabled = true;
        bubble.Padding = Tokens.Space("DvSpace4", "DvSpace2");
        bubble.HorizontalAlignment = HorizontalAlignment.Right;
        bubble.Children.Add(said);
        reply.Padding = bubble.Padding;
        sentBy.HorizontalAlignment = pending.HorizontalAlignment = HorizontalAlignment.Right;
        sentBy.TextAlignment = TextAlignment.Right;

        name.FontWeight = writing.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightBody"];
        name.VerticalAlignment = writing.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAccessibilityView(name, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(writing, AccessibilityView.Raw);
        run.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightBody"];
        run.HorizontalAlignment = HorizontalAlignment.Right;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.Number("DvSpace2"), HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancelEdit);
        buttons.Children.Add(sendEdit);
        editor.Children.Add(edit);
        editor.Children.Add(buttons);
        editor.KeyDown += (_, args) =>
        {
            if (args.Key is Windows.System.VirtualKey.Escape)
            {
                args.Handled = true;
                StopEditing();
            }
        };
        sendEdit.Click += async (_, _) => await SendEdit();
        cancelEdit.Click += (_, _) => StopEditing();
        unusableReply.Retry += async (_, _) => await Act();

        // The focus is read once it has settled: on its way from one part of a reply to the next it is nowhere.
        GotFocus += (_, _) => ShowFocus();
        LostFocus += (_, _) => DispatcherQueue.TryEnqueue(ShowFocus);
        footer.UsageClosed += () => DispatcherQueue.TryEnqueue(ShowFocus);
        Tapped += Pressed;
        KeyDown += KeyPressed;
        SizeChanged += (_, _) => Fit();
        words.LinkPressed += LinkPressed;
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows a message, or what changed of the one already shown: text that arrived, a call that moved on. A message
    /// of an older reply (<paramref name="isNewest"/> false) keeps its actions out of sight until asked.
    /// </summary>
    public void Show(ChatMessage message, ChatEntryParameters parameters, bool isMoment = false, bool isNewest = true)
    {
        var style = ThemeTokens.Current.Surfaces.Conversation;
        var asBefore = parameters == with && isMoment == moment && isNewest == newest && style == laidStyle;
        if (asBefore && Same(message, shown))
        {
            return;
        }

        (shown, with, moment, newest) = (message, parameters, isMoment, isNewest);
        var wanted = ChatRows.Shape(message, parameters.Compact);

        // A theme may set a conversation another way, so what is laid out is for the shape and the style both.
        if (wanted != shape || style != laidStyle)
        {
            (shape, laidStyle) = (wanted, style);
            ring.Child = Lay(wanted, style is ConversationStyle.Bubbles);
        }

        AutomationProperties.SetName(this, ChatRows.EntryName(message, parameters.BotName, parameters.Asker));
        Describe(message, parameters);
        IsTabStop = isMoment || reveals;

        // Words further on in a turn hang close under the message above; one with nothing left to show takes no room.
        Margin = shape is ChatEntryShape.Continued ? new(0, Tokens.Number("DvSpace2") - Tokens.Number("DvSpace5"), 0, 0) : new(0);
        Visibility = Shown(shape is not ChatEntryShape.Continued || HasAnyShown());
        Fit();
        Paint();
    }

    // A conversation is worked out anew each time it changes, so a message that did not change comes as a new
    // object that says the same. Its calls and files are compared one by one; a call under way differs each time.
    private static bool Same(ChatMessage message, ChatMessage? before) =>
        before is not null
        && (ReferenceEquals(message, before)
            || (message with { Tools = before.Tools, Files = before.Files, Sources = before.Sources } == before
                && message.Tools.SequenceEqual(before.Tools)
                && message.Files.SequenceEqual(before.Files)
                && message.Sources.SequenceEqual(before.Sources)));

    private bool HasAnyShown()
    {
        UIElement[] parts = [words, unusableReply, sources, tools, files];
        return hasFooter || parts.Any(part => part.Visibility is Visibility.Visible);
    }

    /// <summary>Puts the focus on the message a link named, so a keyboard or a screen reader lands there too.</summary>
    public void Land() => Focus(FocusState.Programmatic);

    /// <summary>Raised when a press showed this older reply's actions: the list then hides another's.</summary>
    public event Action<ChatEntry>? Pinned;

    /// <summary>Hides the actions a press showed, for when a press shows another reply's.</summary>
    public void Unpin() => Pin(false);

    /// <summary>True for an older reply that keeps actions out of sight until asked: it opens and closes.</summary>
    internal bool Reveals => reveals;

    /// <summary>True while an older reply's actions are in view.</summary>
    internal bool ActionsShown => footer.Visibility is Visibility.Visible || run.Visibility is Visibility.Visible;

    /// <summary>Shows or hides an older reply's actions, as a press does, for assistive technology.</summary>
    internal void ShowActions(bool value)
    {
        if (reveals)
        {
            Pin(value);
        }
    }

    #endregion

    #region Laying Out

    protected override AutomationPeer OnCreateAutomationPeer() => new ChatEntryPeer(this);

    private FrameworkElement Lay(ChatEntryShape wanted, bool bubbles)
    {
        Detach(words, unusableReply, reasoning, ledger, ledgerAfter, sources, tools, files, footer, versions, editor);
        Detach(bubble, reply, sentBy, pending, plate, head, name, writing, run);
        fromRow = null;
        words.HorizontalAlignment = HorizontalAlignment.Stretch;

        return wanted switch
        {
            ChatEntryShape.Person => Column(HorizontalAlignment.Right, bubble, editor, sentBy, files, pending, versions),
            ChatEntryShape.Tools => Column(HorizontalAlignment.Stretch, tools, sources, files, versions),
            ChatEntryShape.Compact => Column(HorizontalAlignment.Stretch, reasoning, ledger, words, unusableReply, ledgerAfter, sources, tools, files),
            ChatEntryShape.Continued => Continued(bubbles),
            _ => bubbles ? SpokenInBubbles() : Spoken(),
        };
    }

    // Words further on in a turn hang under its head, in line with its words: the work done since the words before,
    // the words, the work after them at the turn's end, what they cite, the calls that stay cards, and the footer where
    // the reply ends.
    private StackPanel Continued(bool bubbles)
    {
        footer.Hold(versions);
        if (!bubbles)
        {
            return Column(HorizontalAlignment.Stretch, ledger, words, unusableReply, ledgerAfter, sources, tools, files, footer);
        }

        var column = Column(HorizontalAlignment.Stretch, ledger, InBubble(), unusableReply, ledgerAfter, sources, tools, files, footer);
        reply.HorizontalAlignment = HorizontalAlignment.Left;
        return column;
    }

    // Who speaks, on one line: the bot's head where the theme sets it there, its name, that it is writing, and, at the
    // line's end, the way to what happened.
    private Grid From(bool withHead)
    {
        // A surface no one sees, so a press between the name and the link is a press on this line.
        var from = new Grid
        {
            ColumnSpacing = Tokens.Number("DvSpace2"),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        FrameworkElement[] parts = withHead ? [head, name, writing, run] : [name, writing, run];
        foreach (var part in parts)
        {
            var width = ReferenceEquals(part, run) ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            from.ColumnDefinitions.Add(new() { Width = width });
            Grid.SetColumn(part, from.Children.Count);
            from.Children.Add(part);
        }

        fromRow = from;
        return from;
    }

    // A bot's words in a surface of their own, as wide as they need and no wider than a person's.
    private Facet InBubble()
    {
        if (!reply.Children.Contains(words))
        {
            reply.Children.Add(words);
        }

        return reply;
    }

    // A bot's words where both sides speak in bubbles: its head, small, before its name over the turn's first bubble,
    // then what it reasoned or the work before its words, the words in their bubble, the work after them, and under it
    // at the full width the sources, the calls that stay cards, the files, and the footer.
    private StackPanel SpokenInBubbles()
    {
        head.Size = (int)Tokens.Number("DvAvatar24");
        head.VerticalAlignment = VerticalAlignment.Center;
        footer.Hold(versions);
        var from = From(withHead: true);
        var side = HorizontalAlignment.Stretch;
        var body = Column(side, from, reasoning, ledger, InBubble(), unusableReply, ledgerAfter, sources, tools, files, footer);
        reply.HorizontalAlignment = HorizontalAlignment.Left;
        return body;
    }

    // A bot's words beside its head: the plate, then who speaks, what it reasoned or, for a turn, its work as one list,
    // the words and the sources they cite, the calls that stay cards, and what the reply used.
    private Grid Spoken()
    {
        head.Size = (int)Tokens.Number("DvAvatar48");
        plate.Children.Add(head);
        var from = From(withHead: false);

        // A reply's versions and the way to another reply stand on its footer's one line, after copy.
        footer.Hold(versions);
        var body = Column(HorizontalAlignment.Stretch, from, reasoning, ledger, words, unusableReply, ledgerAfter, sources, tools, files, footer);
        body.Padding = new(0, Tokens.Number("DvSpace1"), 0, 0);

        var row = new Grid { ColumnSpacing = Tokens.Number("DvSpace3") };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(body, 1);
        row.Children.Add(plate);
        row.Children.Add(body);
        return row;
    }

    private static StackPanel Column(HorizontalAlignment side, params FrameworkElement[] parts)
    {
        var column = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        foreach (var part in parts)
        {
            part.HorizontalAlignment = side;
            column.Children.Add(part);
        }

        return column;
    }

    private static void Detach(params FrameworkElement[] parts)
    {
        foreach (var part in parts)
        {
            (part.Parent as Panel)?.Children.Remove(part);
        }
    }

    // A person's words and files keep to their side and never run the full width. Tool calls made without words
    // stand in line with the bot's words; a phone gives them the full width, and so does handed work, where the
    // bot's head stands beside the card.
    private void Fit()
    {
        if (ActualWidth <= 0 || with is null)
        {
            return;
        }

        var most = Math.Floor(ActualWidth * BubbleShare);
        if (bubble.MaxWidth != most)
        {
            bubble.MaxWidth = sentBy.MaxWidth = reply.MaxWidth = most;
        }

        // Where both sides speak in bubbles no plate stands beside the words, so nothing is set in to clear one.
        var wide = XamlRoot is { } root && root.Size.Width >= (double)Styles.Dictionary["DvBreakPhone"];
        var under = laidStyle is ConversationStyle.Bubbles
            ? 0
            : Tokens.Number("DvAvatar48") + (2 * Tokens.Number("DvSpace1")) + (2 * Tokens.Number("DvBorder"))
                + Tokens.Number("DvSpace3");
        var inset = shape is ChatEntryShape.Tools && !with.Compact && wide ? under : 0;

        // Words further on in a turn line up with the turn's words, beside where its head stands.
        if (shape is ChatEntryShape.Continued && ring.Child is FrameworkElement laid)
        {
            laid.Margin = new(wide ? under : 0, 0, 0, 0);
        }

        var most2 = shape is ChatEntryShape.Person ? most : double.PositiveInfinity;

        if (tools.Margin.Left != inset)
        {
            tools.Margin = new(inset, 0, 0, 0);
            files.Margin = sources.Margin = shape is ChatEntryShape.Tools ? new(inset, 0, 0, 0) : new(0);
        }

        if (files.MaxWidth != most2)
        {
            files.MaxWidth = most2;
        }
    }

    #endregion

    #region Showing

    private void Describe(ChatMessage message, ChatEntryParameters parameters)
    {
        said.Text = message.Text;
        editing = editing && message.MayEdit;
        bubble.Visibility = Shown(message.FromPerson && message.Text.Length > 0 && !editing);
        editor.Visibility = Shown(editing);
        edit.Label = ChatRows.EditFieldName(parameters.BotName);
        edit.Hint = ChatRows.EditHint(parameters.BotName);
        sentBy.Text = message.SentBy ?? string.Empty;
        sentBy.Visibility = Shown(message.SentBy is not null);
        pending.Text = ChatRows.Pending(parameters.BotName);
        pending.Visibility = Shown(message.Pending);

        head.BotName = parameters.BotName;
        head.Hue = parameters.BotHue;
        head.State = message switch
        {
            { Thinking: true } => AvatarState.Thinking,
            { Streaming: true } => AvatarState.Writing,
            _ => AvatarState.Rest,
        };
        name.Text = parameters.BotName;
        writing.Text = ChatRows.Arriving(message);
        writing.Visibility = Shown(message.Streaming);
        run.Address = parameters.RunAddress is { } address && message.ActivityRunId is { } activityRunId ? address(activityRunId) : null;
        leads = run.Address is not null && shape is ChatEntryShape.Bot;

        var unusable = !message.FromPerson && message.UnusableToolRequest;

        if (!message.FromPerson && message.Text.Length > 0 && !unusable)
        {
            words.Streaming = message.Streaming;
            words.Text = message.Text;
        }

        words.Visibility = reply.Visibility = Shown(message.Text.Length > 0 && !unusable);
        unusableReply.Visibility = Shown(unusable);
        if (unusable)
        {
            unusableReply.MessageKey = message.Key;
            unusableReply.BotName = parameters.BotName;
            unusableReply.OriginalResponse = message.Text;
            unusableReply.CanRetry = ChatRows.OffersUnusableRetry(message, parameters.Regenerate is not null);
        }
        // In a turn, the reasoning and the calls that fold are rows of the work between the bot's words.
        var inTurn = message.Ledger is not null || message.LedgerAfter is not null || message.ContinuesTurn;
        reasoning.Visibility = Shown(message.Reasoning is not null && !inTurn);
        ShowWork(ledger, message.Ledger, parameters);
        ShowWork(ledgerAfter, message.LedgerAfter, parameters);

        if (message.Reasoning is { } reasoned && !inTurn)
        {
            reasoning.Show(reasoned, message.Thinking);
        }

        sources.Sources = message.Sources;
        sources.Visibility = Shown(message.Sources.Count > 0);

        var cards = message.Tools.Where(tool => !inTurn || !TurnLedger.Folds(tool)).ToList();
        Keyed.Sync(tools, cards, tool => $"{tool.Id}:{ChatRows.Shape(tool, parameters.Takeovers)}", tool => Card(tool, parameters), (card, tool) => Tell(card, tool, parameters));
        tools.Visibility = Shown(cards.Count > 0);
        Keyed.Sync(files, message.Files, file => file.Id.ToString(), _ => new FileCard(), (card, file) =>
        {
            card.File = file;
            card.ReadWhole = parameters.ReadWhole;
            card.HorizontalAlignment = message.FromPerson ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        });
        files.Visibility = Shown(message.Files.Count > 0);

        ShowUsage(message, parameters);
        ShowVersions(message, parameters);
        ShowFooter(message, parameters);

        reveals = ChatRows.RevealsActions(message, newest, hasFooter, leads);
        pinned = pinned && reveals;
        ShowActions();
    }

    // The footer is for a finished reply: copy, the usage of the reply, another reply, and its versions.
    private void ShowFooter(ChatMessage message, ChatEntryParameters parameters)
    {
        hasFooter = false;
        if (shape is ChatEntryShape.Person or ChatEntryShape.Tools or ChatEntryShape.Compact)
        {
            return;
        }

        // While the bot writes, nothing stands under its words.
        footer.OnRegenerate = parameters.Regenerate is not null ? Act : null;
        var parts = ChatRows.FooterParts(
            message,
            parameters.Regenerate is not null,
            parameters.ShowVersion is not null,
            parameters.LoadUsage is not null);
        footer.Show(message.Text, parts);
        hasFooter = !message.Streaming && footer.HasAny;
    }

    // The newest reply keeps its actions in view. An older one shows them while asked: the keyboard's focus is in it,
    // a press showed them, or the popover of what it used is open. Actions that go while the focus is on one of them
    // leave the focus on the reply, not on a control that is no longer there.
    private void ShowActions()
    {
        var asked = ChatRows.ShowsActions(newest, focusWithin || pinned || footer.UsageOpen);
        var before = ActionsShown;
        var (footerShown, runShown) = (hasFooter && asked, leads && asked);

        if ((!footerShown && Focused(footer) is not null) || (!runShown && Focused(run) is not null))
        {
            var state = (Focused(footer) ?? Focused(run))!.FocusState;
            Focus(state is FocusState.Pointer ? FocusState.Pointer : FocusState.Programmatic);
        }

        footer.Visibility = Shown(footerShown);
        run.Visibility = Shown(runShown);

        if (reveals && before != ActionsShown && FrameworkElementAutomationPeer.FromElement(this) is ChatEntryPeer peer)
        {
            peer.Changed(before);
        }
    }

    // What has the focus, when it is the given part of the reply or inside it.
    private UIElement? Focused(FrameworkElement part)
    {
        if (XamlRoot is not { } root || FocusManager.GetFocusedElement(root) is not UIElement focused)
        {
            return null;
        }

        DependencyObject? at = focused;
        while (at is not null && !ReferenceEquals(at, this))
        {
            if (ReferenceEquals(at, part))
            {
                return focused;
            }

            at = VisualTreeHelper.GetParent(at);
        }

        return null;
    }

    private void ShowVersions(ChatMessage message, ChatEntryParameters parameters)
    {
        versions.ShowVersion = parameters.ShowVersion;
        versions.OffersReplyAction = shape is not (ChatEntryShape.Bot or ChatEntryShape.Continued);
        versions.OnAct = (message.FromPerson ? parameters.Edit is not null : parameters.Regenerate is not null) ? () => Act() : null;
        versions.Show(message);
        versions.Visibility = Shown(versions.HasAny && !editing);
    }

    // A person's message opens its edit in place of its words; a reply asks for another at once.
    private async Task Act()
    {
        if (shown is not { } message || with is not { } parameters || !Guid.TryParse(message.Key, out var id))
        {
            return;
        }

        if (!message.FromPerson)
        {
            await (parameters.Regenerate?.Invoke(id) ?? Task.CompletedTask);
            return;
        }

        editing = true;
        edit.Value = message.Text;
        Describe(message, parameters);
        Pieces.FocusWhenShown(edit, edit.TakeFocus);
    }

    private async Task SendEdit()
    {
        if (shown is not { } message || with?.Edit is not { } send || !Guid.TryParse(message.Key, out var id) || string.IsNullOrWhiteSpace(edit.Value))
        {
            return;
        }

        editing = false;
        Describe(message, with);
        await send(id, edit.Value);
    }

    private void StopEditing()
    {
        if (shown is not { } message || with is null)
        {
            return;
        }

        editing = false;
        Describe(message, with);
        Pieces.FocusWhenShown(versions, versions.TakeFocus);
    }

    // One view model for a run, kept while the reply is of that run: its words arrive again with the model that wrote
    // them, and what was read must not be lost with the first of them.
    private void ShowUsage(ChatMessage message, ChatEntryParameters parameters)
    {
        var offered = ChatRows.OffersUsage(message, parameters.LoadUsage is not null) ? message.RunId : null;

        if (offered != usageRun)
        {
            usageRun = offered;
            footer.Model = offered is { } runId
                ? new(runId, parameters.LoadUsage!, Log.For<ReplyFooter>(), message.Model)
                : null;
        }

        footer.Model?.Model = message.Model;
    }

    private static FrameworkElement Card(ToolCard tool, ChatEntryParameters parameters) => ChatRows.Shape(tool, parameters.Takeovers) switch
    {
        ToolCardShape.Delegation => new DelegationCard(),
        ToolCardShape.Takeover => new TakeoverCall(),
        _ => new ToolCallCard(),
    };

    private static void Tell(FrameworkElement card, ToolCard tool, ChatEntryParameters parameters)
    {
        switch (card)
        {
            case DelegationCard handed when tool.Delegation is { } work:
                handed.Item = work;
                handed.ChildContent = work.Work.Count > 0 ? WorkOf(handed.ChildContent as StackPanel, work, parameters) : null;
                break;
            case TakeoverCall call when tool.Takeover is { } takeover && parameters.Takeovers is { } takeovers:
                call.Takeover = takeover;
                call.BotName = parameters.BotName;
                call.Takeovers = takeovers;
                break;
            case ToolCallCard plain:
                plain.Card = tool;
                plain.BotName = parameters.BotName;
                plain.Decide = parameters.Decide;
                plain.Forget = parameters.Forget;
                plain.Forgotten = tool.NoteId is { } noteId && parameters.ForgottenNotes.Contains(noteId);
                plain.MemoryHref = parameters.MemoryHref;
                break;
        }
    }

    // The other bot's own messages, inside the card of the work handed to it. A waiting call there is asked for by
    // the bot that made it, and that bot works on its own screen.
    private static StackPanel WorkOf(StackPanel? kept, DelegationItem work, ChatEntryParameters parameters)
    {
        var column = kept ?? new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        var beneath = new ChatEntryParameters(work.ChildName, BotIdentity.Hue(work.ChildId))
        {
            Compact = true,
            Decide = parameters.Decide,
            ReadWhole = parameters.ReadWhole,
            Takeovers = ChatRows.ForBotBeneath(parameters.Takeovers, work),
        };

        Keyed.Sync(column, work.Work, entry => entry.Key, _ => new ChatEntry(), (entry, message) => entry.Show(message, beneath));
        return column;
    }

    private static Visibility Shown(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    private static void ShowWork(WorkLedger ledger, IReadOnlyList<LedgerStep>? work, ChatEntryParameters parameters)
    {
        ledger.Visibility = Shown(work is not null);
        if (work is not null)
        {
            ledger.Parameters = parameters;
            ledger.Show(work);
        }
    }

    private void Paint()
    {
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var bubbles = ThemeTokens.Current.Surfaces.Conversation is ConversationStyle.Bubbles;
        var ringed = (moment || reveals) && FocusState is not FocusState.Unfocused;
        ring.BorderBrush = ringed ? Tokens.Brush(this, "DvEmber") : clear;
        plate.Edge = shown?.Streaming is true ? Tokens.Brush(this, "DvIon") : null;

        // A bubble where both sides speak in bubbles has no line around it: its edge is its own surface.
        bubble.Fill = Tokens.Brush(this, "DvBubble");
        bubble.Edge = bubbles ? bubble.Fill : null;
        reply.Fill = reply.Edge = Tokens.Brush(this, "DvBg2");
        writing.Foreground = Tokens.Brush(this, "DvIon");
        name.Foreground = Tokens.Brush(this, "DvText3");
    }

    #endregion

    #region Showing An Older Reply's Actions

    // The keyboard's focus in an older reply shows its actions, which are then the next stops. A press that moved the
    // focus does not count: a press shows and hides them itself.
    private void ShowFocus()
    {
        focusWithin = HoldsKeyboardFocus();
        ShowActions();
        Paint();
    }

    private bool HoldsKeyboardFocus()
    {
        if (XamlRoot is not { } root
            || FocusManager.GetFocusedElement(root) is not UIElement focused
            || focused.FocusState is FocusState.Unfocused or FocusState.Pointer)
        {
            return false;
        }

        for (DependencyObject? at = focused; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (ReferenceEquals(at, this))
            {
                return true;
            }
        }

        return false;
    }

    // A press on an older reply's words or on its name shows its actions, and a second press hides them. A press on a
    // button, a field or a card is that control's own, a press that ends a selection of the words selects, and a
    // press on a link in the words opens the link.
    private void Pressed(object sender, TappedRoutedEventArgs press)
    {
        var source = press.OriginalSource;
        if (!reveals || linkPressed || !OnWords(source as DependencyObject) || HasSelection(source))
        {
            return;
        }

        Pin(!pinned);
        pressToggled = true;
        DispatcherQueue.TryEnqueue(() => pressToggled = false);
    }

    private void LinkPressed()
    {
        if (pressToggled)
        {
            pressToggled = false;
            Pin(!pinned);
        }

        linkPressed = true;
        DispatcherQueue.TryEnqueue(() => linkPressed = false);
    }

    // Enter or Space on the reply itself does what a press does, so its actions stay once the focus moves on.
    private void KeyPressed(object sender, KeyRoutedEventArgs key)
    {
        if (reveals && ReferenceEquals(key.OriginalSource, this) && key.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            key.Handled = true;
            Pin(!pinned);
        }
    }

    private bool OnWords(DependencyObject? pressed)
    {
        for (var at = pressed; at is not null && !ReferenceEquals(at, this); at = VisualTreeHelper.GetParent(at))
        {
            if (at is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase or TextBox or ChatEntry or Disclosure)
            {
                return false;
            }

            if (ReferenceEquals(at, words) || ReferenceEquals(at, reply) || ReferenceEquals(at, unusableReply) || ReferenceEquals(at, fromRow))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSelection(object pressed) => pressed is TextBlock { SelectedText.Length: > 0 };

    private void Pin(bool value)
    {
        if (pinned == value)
        {
            return;
        }

        pinned = value;
        ShowActions();

        if (value)
        {
            Pinned?.Invoke(this);
        }
    }

    #endregion
}
