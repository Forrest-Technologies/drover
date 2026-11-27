namespace Drover.Native.Chat;

/// <summary>
/// One step of a turn's ledger on one line: an icon for the kind of work, its label, the command or path, how long
/// it took, and its state as a small icon (a check when done, a cross when failed, a still mark when stopped), or the
/// word "Running" while it runs, while the line travels around the ledger's frame. A failed step says why on a second line.
/// The row is a button: its name says all of it and whether it is open, and opening it shows the call in place, its
/// command or arguments and then what it printed and returned, drawn by what they hold. A group opens to its calls;
/// reasoning opens to its words.
/// </summary>
internal sealed class LedgerRow : StackPanel
{
    #region Private Fields

    private readonly Grid top = new();

    private readonly Border divider = new();

    private readonly PlainButton toggle = new() { HoverFill = "DvBg2", RingInside = true, Radius = null };

    private readonly Icon kind = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock label = Styles.Text(null, "DvTextLabel");

    private readonly TextBlock summary = Styles.Text(null, "DvTextMono");

    private readonly TextBlock failure = Styles.Text(null, "DvTextCaption");

    private readonly TextBlock duration = Styles.Text(null, "DvTextCaption");

    private readonly Icon state = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly TextBlock running = Styles.Text(LedgerRunning, "DvTextCaption");

    private readonly Icon mark = new() { VerticalAlignment = VerticalAlignment.Center };

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace3") };

    private readonly OutputBox liveOutput = new() { Label = ToolCallCardViewModel.OutputTitle, FollowsEnd = true, CopyLabel = OutputWords.CopyOutput };

    private LedgerStep? step;

    private LedgerStep? drawn;

    private bool open;

    // What the person chose by opening or closing the row. Until they choose, a running call opens by itself.
    private bool? chosen;

    #endregion

    #region Constructors

    public LedgerRow()
    {
        label.TextTrimming = summary.TextTrimming = TextTrimming.CharacterEllipsis;
        label.TextWrapping = summary.TextWrapping = TextWrapping.NoWrap;
        label.VerticalAlignment = summary.VerticalAlignment = duration.VerticalAlignment = running.VerticalAlignment = VerticalAlignment.Center;
        failure.TextWrapping = TextWrapping.NoWrap;
        failure.TextTrimming = TextTrimming.CharacterEllipsis;

        var line = new Grid { ColumnSpacing = Tokens.Number("DvSpace2") };
        GridLength[] widths = [GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto, GridLength.Auto];
        foreach (var width in widths)
        {
            line.ColumnDefinitions.Add(new() { Width = width });
        }

        Place(line, kind, 0);
        Place(line, label, 1);
        Place(line, summary, 2);
        Place(line, duration, 3);
        var stateCell = new Grid { VerticalAlignment = VerticalAlignment.Center };
        stateCell.Children.Add(state);
        stateCell.Children.Add(running);
        Place(line, stateCell, 4);
        Place(line, mark, 5);

        var words = new StackPanel();
        words.Children.Add(line);
        words.Children.Add(failure);
        UIElement[] read = [kind, label, summary, duration, state, running, mark, failure];
        foreach (var part in read)
        {
            AutomationProperties.SetAccessibilityView(part, AccessibilityView.Raw);
        }

        toggle.Content = words;
        toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
        toggle.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        toggle.Padding = Tokens.Space("DvSpace3", "DvSpace2");
        toggle.Click += (_, _) => Toggle();
        ArrowKeyZone.SetIsItem(toggle, true);
        ToolTipService.SetToolTip(state, null);

        divider.Height = Tokens.Number("DvBorder");
        top.Children.Add(toggle);
        body.Padding = new(Tokens.Number("DvSpace3"), 0, Tokens.Number("DvSpace3"), Tokens.Number("DvSpace3"));
        body.Visibility = Visibility.Collapsed;
        Children.Add(divider);
        Children.Add(top);
        Children.Add(body);
        Loaded += (_, _) => toggle.MinHeight = Targets.Minimum;
        this.WhenShownOrRethemed(Paint);
    }

    #endregion

    #region Properties

    /// <summary>The parameters the conversation draws its tool calls with.</summary>
    public ChatEntryParameters? Parameters { get; set; }

    /// <summary>True for every row but the first, which a line sets apart from the one above.</summary>
    public bool Divided
    {
        get => divider.Visibility is Visibility.Visible;
        set => divider.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows the step. A call opens by itself while it runs, so its output can be watched as it arrives, and closes
    /// when it ends, unless the person opened or closed the row themselves.
    /// </summary>
    public void Show(LedgerStep shown)
    {
        step = shown;
        var wanted = chosen ?? (shown.Reasoning is null && TurnLedger.StateOf(shown) is LedgerState.Running);
        if (wanted != open)
        {
            SetOpen(wanted);
            return;
        }

        Paint();

        if (open && !Equals(shown, drawn))
        {
            DrawBody();
        }
    }

    #endregion

    #region Private Methods

    private static void Place(Grid line, FrameworkElement part, int column)
    {
        Grid.SetColumn(part, column);
        line.Children.Add(part);
    }

    private void Toggle()
    {
        chosen = !open;
        SetOpen(!open);
    }

    private void SetOpen(bool value)
    {
        open = value;
        body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;

        if (open)
        {
            DrawBody();
        }
        else
        {
            body.Children.Clear();
            drawn = null;
        }

        Paint();
    }

    private void Paint()
    {
        if (step is not { } shown)
        {
            return;
        }

        var now = TurnLedger.StateOf(shown);
        var icon = shown.Reasoning is not null ? new ToolIcon(IconName.Thought) : ToolIcons.For(shown.Calls[0].Tool);
        kind.NameOfIcon = icon.Shape;
        kind.ColorToken = icon.ColorToken;
        label.Text = TurnLedger.Label(shown);
        label.Foreground = Tokens.Brush(this, "DvText");
        summary.Text = TurnLedger.Summary(shown);
        summary.Foreground = Tokens.Brush(this, "DvText2");
        duration.Text = TurnLedger.Duration(shown) ?? string.Empty;
        duration.Visibility = duration.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        duration.Foreground = Tokens.Brush(this, "DvText3");
        failure.Text = TurnLedger.FailureLine(shown) ?? string.Empty;
        failure.Visibility = failure.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        failure.Foreground = Tokens.Brush(this, "DvDanger");

        running.Visibility = now is LedgerState.Running ? Visibility.Visible : Visibility.Collapsed;
        running.Foreground = Tokens.Brush(this, "DvIon");
        state.Visibility = now is LedgerState.Running ? Visibility.Collapsed : Visibility.Visible;
        (state.NameOfIcon, state.Foreground) = now switch
        {
            LedgerState.Failed => (IconName.Close, Tokens.Brush(this, "DvDanger")),
            LedgerState.Stopped => (IconName.Dot, Tokens.Brush(this, "DvText3")),
            _ => (IconName.Check, Tokens.Brush(this, "DvText3")),
        };

        mark.NameOfIcon = open ? IconName.ChevronDown : IconName.ChevronRight;
        mark.Foreground = Tokens.Brush(this, "DvText3");
        divider.Background = Tokens.Brush(this, "DvLine");

        var name = TurnLedger.RowName(shown);
        AutomationProperties.SetName(toggle, name);
        AutomationProperties.SetItemStatus(toggle, open ? "Expanded" : "Collapsed");
        ToolTipService.SetToolTip(toggle, name);
    }

    #endregion

    #region Opened

    private void DrawBody()
    {
        if (step is not { } shown)
        {
            return;
        }

        drawn = shown;
        body.Children.Clear();

        switch (shown)
        {
            case { Reasoning: { } reasoning }:
                body.Children.Add(new MarkdownView { Text = reasoning, Streaming = shown.Thinking });
                break;
            case { IsGroup: true }:
                var calls = new StackPanel();
                foreach (var call in shown.Calls)
                {
                    var row = new LedgerRow { Parameters = Parameters, Divided = calls.Children.Count > 0 };
                    row.Show(new LedgerStep([call], null, false));
                    calls.Children.Add(row);
                }

                body.Children.Add(calls);
                break;
            default:
                DrawCall(shown.Calls[0]);
                break;
        }
    }

    // The call as it was asked and as it ended: its command or arguments, then what it printed, then its result.
    private void DrawCall(ToolCard card)
    {
        if (card.Tool is "sandbox.run" && CommandOf(card) is { Length: > 0 } command)
        {
            body.Children.Add(new CodeBlock { Code = command, CodeLanguage = CodeContent.ShellLabel, Bounded = true });
        }
        else if (card.Facts.Count > 0)
        {
            body.Children.Add(Pieces.Facts(card.Facts));
        }
        else if (!card.ShowsAllArguments)
        {
            body.Children.Add(Pieces.Part(ToolCallCardViewModel.ArgumentsTitle, new CodeBlock { Code = card.Arguments, CodeLanguage = "json", Bounded = true }));
        }

        if (card.Output.Count > 0)
        {
            body.Children.Add(Pieces.Part(ToolCallCardViewModel.OutputTitle, Output(card)));
        }

        if (card.Result is { } result || card.ExitCode is not null)
        {
            var part = Pieces.Part(ToolCallCardViewModel.ResultTitle);
            if (TurnLedger.ExitLine(card) is { } exit)
            {
                part.Children.Add(Pieces.Quiet(exit));
            }

            if (card.Result is { } text)
            {
                part.Children.Add(new CodeBlock { Code = text, CodeLanguage = card.ResultLanguage, Bounded = true, CopyLabel = OutputWords.CopyOutput });
            }

            body.Children.Add(part);
        }
    }

    // While the call runs, its output streams into one box. Once it ended, what it printed is drawn by what it holds,
    // and its error stream stays tagged and colored under it.
    private StackPanel Output(ToolCard card)
    {
        var shown = new StackPanel { Spacing = Tokens.Number("DvSpace2") };
        (liveOutput.Parent as Panel)?.Children.Remove(liveOutput);

        if (card.IsLive)
        {
            liveOutput.Show(null, null, card.Output);
            shown.Children.Add(liveOutput);
            return shown;
        }

        var printed = OutputWords.CopyText(card.Output.Where(piece => piece.Stream is not OutputWords.ErrorStream));
        List<ToolOutputPiece> errors = [.. card.Output.Where(piece => piece.Stream is OutputWords.ErrorStream)];

        if (printed.Length > 0)
        {
            shown.Children.Add(new CodeBlock { Code = printed, CodeLanguage = ToolCallCardViewModel.OutputTitle, Bounded = true, CopyLabel = OutputWords.CopyOutput });
        }

        if (errors.Count > 0)
        {
            liveOutput.Show(null, null, errors);
            shown.Children.Add(liveOutput);
        }

        return shown;
    }

    private static string? CommandOf(ToolCard card) =>
        ToolResults.Read(card.Arguments) is { Form: ResultForm.Whole } read ? ToolResults.StringOf(read.Value, "command") : null;

    #endregion

    #region Fixed Words

    public const string LedgerRunning = "Running";

    #endregion
}
