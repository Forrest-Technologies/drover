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
/// shown by this same control. A bot speaks from its head: the character stands on a faceted plate beside its
/// words, and the plate's edge takes the live color while the bot is writing, and only then.
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

    private readonly ReplyReasoning reasoning = new();

    // The turn's work as one list, on the turn's first message.
    private readonly WorkLedger ledger = new();

    private readonly ReplySources sources = new();

    private readonly StackPanel tools = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly StackPanel files = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly Facet bubble = new();

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

    private string? usageModel;

    private bool moment;

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

        plate.Cut = Tokens.Number("DvCutS");
        plate.Padding = new(Tokens.Number("DvSpace1"));
        plate.VerticalAlignment = VerticalAlignment.Top;
        plate.Children.Add(head);

        said.IsTextSelectionEnabled = true;
        bubble.Padding = Tokens.Space("DvSpace4", "DvSpace2");
        bubble.HorizontalAlignment = HorizontalAlignment.Right;
        bubble.Children.Add(said);
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

        GotFocus += (_, _) => Paint();
        LostFocus += (_, _) => Paint();
        SizeChanged += (_, _) => Fit();
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Public Methods

    /// <summary>Shows a message, or what changed of the one already shown: text that arrived, a call that moved on.</summary>
    public void Show(ChatMessage message, ChatEntryParameters parameters, bool isMoment = false)
    {
        if (Same(message, shown) && parameters == with && isMoment == moment)
        {
            return;
        }

        (shown, with, moment) = (message, parameters, isMoment);
        var wanted = ChatRows.Shape(message, parameters.Compact);

        if (wanted != shape)
        {
            shape = wanted;
            ring.Child = Lay(wanted);
        }

        AutomationProperties.SetName(this, ChatRows.EntryName(message, parameters.BotName, parameters.Asker));
        IsTabStop = isMoment;
        Describe(message, parameters);

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
        UIElement[] parts = [words, sources, tools, files, footer];
        return parts.Any(part => part.Visibility is Visibility.Visible);
    }

    /// <summary>Puts the focus on the message a link named, so a keyboard or a screen reader lands there too.</summary>
    public void Land() => Focus(FocusState.Programmatic);

    #endregion

    #region Laying Out

    protected override AutomationPeer OnCreateAutomationPeer() => new RolePeer(this, AutomationControlType.Group);

    private FrameworkElement Lay(ChatEntryShape wanted)
    {
        Detach(words, reasoning, ledger, sources, tools, files, footer, versions, editor, bubble, sentBy, pending, plate, name, writing, run);

        return wanted switch
        {
            ChatEntryShape.Person => Column(HorizontalAlignment.Right, bubble, editor, sentBy, files, pending, versions),
            ChatEntryShape.Tools => Column(HorizontalAlignment.Stretch, tools, sources, files, versions),
            ChatEntryShape.Compact => Column(HorizontalAlignment.Stretch, reasoning, words, sources, tools, files),
            ChatEntryShape.Continued => Continued(),
            _ => Spoken(),
        };
    }

    // Words further on in a turn hang under its head, in line with its words: the words, what they cite, the calls that
    // stay cards, and the footer where the reply ends.
    private StackPanel Continued()
    {
        footer.Hold(versions);
        return Column(HorizontalAlignment.Stretch, words, sources, tools, files, footer);
    }

    // A bot's words beside its head: the plate, then who speaks, what it reasoned or, for a turn, its work as one list,
    // the words and the sources they cite, the calls that stay cards, and what the reply used.
    private Grid Spoken()
    {
        var from = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        from.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        from.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        from.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        Grid.SetColumn(writing, 1);
        Grid.SetColumn(run, 2);
        from.Children.Add(name);
        from.Children.Add(writing);
        from.Children.Add(run);

        // A reply's versions and the way to another reply stand on its footer's one line, after copy.
        footer.Hold(versions);
        var body = Column(HorizontalAlignment.Stretch, from, reasoning, ledger, words, sources, tools, files, footer);
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
            bubble.MaxWidth = sentBy.MaxWidth = most;
        }

        var wide = XamlRoot is { } root && root.Size.Width >= (double)Styles.Dictionary["DvBreakPhone"];
        var under = Tokens.Number("DvAvatar48") + (2 * Tokens.Number("DvSpace1")) + (2 * Tokens.Number("DvBorder")) + Tokens.Number("DvSpace3");
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
        run.Visibility = Shown(run.Address is not null);

        if (!message.FromPerson && message.Text.Length > 0)
        {
            words.Streaming = message.Streaming;
            words.Text = message.Text;
        }

        words.Visibility = Shown(message.Text.Length > 0);
        // In a turn, the reasoning and the calls that fold are rows of the turn's ledger, on its first message.
        var inTurn = message.Ledger is not null || message.ContinuesTurn;
        reasoning.Visibility = Shown(message.Reasoning is not null && !inTurn);
        ledger.Visibility = Shown(message.Ledger is not null);
        if (message.Ledger is { } work)
        {
            ledger.Parameters = parameters;
            ledger.Show(work);
        }

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
    }

    // The footer is for a finished reply: copy, another reply, and the hint of what wrote it and what it used.
    private void ShowFooter(ChatMessage message, ChatEntryParameters parameters)
    {
        if (shape is ChatEntryShape.Person or ChatEntryShape.Tools or ChatEntryShape.Compact)
        {
            return;
        }

        // While the bot writes, nothing stands under its words.
        footer.OnRegenerate = parameters.Regenerate is not null ? Act : null;
        footer.Show(message.Text, ChatRows.FooterParts(message, parameters.Regenerate is not null, parameters.ShowVersion is not null, parameters.LoadUsage is not null));
        footer.Visibility = Shown(!message.Streaming && footer.HasAny);
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

    private void ShowUsage(ChatMessage message, ChatEntryParameters parameters)
    {
        var offered = ChatRows.OffersUsage(message, parameters.LoadUsage is not null) ? message.RunId : null;

        if (offered != usageRun || message.Model != usageModel)
        {
            (usageRun, usageModel) = (offered, message.Model);
            footer.Model = offered is { } runId ? new(runId, parameters.LoadUsage!, Log.For<ReplyFooter>(), message.Model) : null;
        }
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

    private void Paint()
    {
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ring.BorderBrush = moment && FocusState is not FocusState.Unfocused ? Tokens.Brush(this, "DvEmber") : clear;
        plate.Edge = shown?.Streaming is true ? Tokens.Brush(this, "DvIon") : null;
        bubble.Fill = Tokens.Brush(this, "DvBg2");
        writing.Foreground = Tokens.Brush(this, "DvIon");
        name.Foreground = Tokens.Brush(this, "DvText3");
    }

    #endregion
}
