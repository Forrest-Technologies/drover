namespace Drover.Native.Chat;

/// <summary>
/// A tool call: a compact card in the bot's turn. Closed, it reads what the bot did, its arguments on one line, and
/// its state in words. Its top edge carries the state: a traveling line while the tool runs, then a still line in
/// green or red once it has ended. A call that waits for a person is a question with its two answers instead.
/// </summary>
public sealed class ToolCallCard : Grid
{
    #region Private Fields

    private readonly ToolCallCardViewModel model = new();

    private readonly Batch batch;

    // The card that opens. Neither form shows before the card has drawn what it was given.
    private readonly StackPanel card = new() { Visibility = Visibility.Collapsed };

    private readonly Disclosure section = new() { TitleWeight = "DvWeightLabel", SummaryWraps = false };

    private readonly Badge state = new() { Wraps = true };

    private readonly Border endedEdge = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };

    private readonly StackPanel body = new() { Spacing = Tokens.Number("DvSpace4") };

    private readonly OutputBox output = new() { Label = ToolCallCardViewModel.OutputTitle, FollowsEnd = true };

    private readonly TextBlock under = Styles.Text(null);

    private readonly Border underBox = new();

    private readonly StackPanel note = new() { Spacing = Tokens.Number("DvSpace2"), HorizontalAlignment = HorizontalAlignment.Left };

    private readonly Button forget = new() { Content = ToolCallCardViewModel.ForgetLabel, HorizontalAlignment = HorizontalAlignment.Left };

    private readonly FocusLine noteLine = new();

    private readonly StatusNote forgetProblem = new() { Kind = NoteKind.Danger };

    // The question, for a call that waits for a person.
    private readonly Facet ask = new() { Visibility = Visibility.Collapsed };

    private readonly TextBlock askTitle = Styles.Text(null);

    private readonly Badge askState = new() { Kind = BadgeKind.Warn, Wraps = true };

    private readonly ContentPresenter askDetails = new();

    private readonly Button approve = new() { Kind = ButtonKind.Primary, Content = ToolCallCardViewModel.Approve, IsEnabled = false };

    private readonly Button deny = new() { Content = ToolCallCardViewModel.Deny, IsEnabled = false };

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private ToolCard? drawn;

    private bool drawnOpen;

    #endregion

    #region Constructors

    public ToolCallCard()
    {
        batch = new(this, Apply, Draw);
        BuildCard();
        BuildAsk();
        Children.Add(card);
        Children.Add(ask);
        model.PropertyChanged += (_, _) => batch.Draw();
        this.WhenShownOrRethemed(batch.Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty CardProperty = Dp.Register<ToolCallCard, ToolCard?>(nameof(Card), null, card => card.batch.Values());

    public static readonly DependencyProperty BotNameProperty = Dp.Register<ToolCallCard, string>(nameof(BotName), string.Empty, card => card.batch.Values());

    public static readonly DependencyProperty DecideProperty =
        Dp.Register<ToolCallCard, Func<Guid, bool, Task<string?>>?>(nameof(Decide), null, card => card.batch.Values());

    public static readonly DependencyProperty ForgetProperty =
        Dp.Register<ToolCallCard, Func<Guid, string, Task<ForgetOutcome>>?>(nameof(Forget), null, card => card.batch.Values());

    public static readonly DependencyProperty ForgottenProperty = Dp.Register<ToolCallCard, bool>(nameof(Forgotten), false, card => card.batch.Values());

    public static readonly DependencyProperty MemoryHrefProperty = Dp.Register<ToolCallCard, string?>(nameof(MemoryHref), null, card => card.batch.Values());

    /// <summary>The call to show. Required.</summary>
    public ToolCard? Card
    {
        get => (ToolCard?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    /// <summary>The bot that made the call. Required.</summary>
    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>Sends a person's answer to a call that waits for one. Returns why it failed, or nothing when it was sent.</summary>
    public Func<Guid, bool, Task<string?>>? Decide
    {
        get => (Func<Guid, bool, Task<string?>>?)GetValue(DecideProperty);
        set => SetValue(DecideProperty, value);
    }

    /// <summary>Removes the note a save kept, from its card, while it still says what the card shows.</summary>
    public Func<Guid, string, Task<ForgetOutcome>>? Forget
    {
        get => (Func<Guid, string, Task<ForgetOutcome>>?)GetValue(ForgetProperty);
        set => SetValue(ForgetProperty, value);
    }

    /// <summary>True once the card's note was forgotten, until that is undone.</summary>
    public bool Forgotten
    {
        get => (bool)GetValue(ForgottenProperty);
        set => SetValue(ForgottenProperty, value);
    }

    /// <summary>The bot's memory, where a note that changed since this card can be seen.</summary>
    public string? MemoryHref
    {
        get => (string?)GetValue(MemoryHrefProperty);
        set => SetValue(MemoryHrefProperty, value);
    }

    #endregion

    #region Layout

    // The card is drawn from the call it was given before it is first measured, so it never shows as an empty frame.
    protected override Size MeasureOverride(Size availableSize)
    {
        batch.DrawIfWaiting();
        return base.MeasureOverride(availableSize);
    }

    #endregion

    #region Building

    private void BuildCard()
    {
        section.Status = state;
        section.Body = body;
        section.RegisterPropertyChangedCallback(Disclosure.OpenProperty, (_, _) => model.Open = section.Open);

        // The still line of a tool that has ended lies on the frame's top edge, past its cut corner.
        endedEdge.Height = Tokens.Number("DvFocusWidth");
        endedEdge.Margin = new(Tokens.Number("DvCut"), 0, 0, 0);
        var framed = new Grid();
        framed.Children.Add(section);
        framed.Children.Add(endedEdge);

        under.FontSize = Tokens.Number("DvText13");
        under.LineHeight = (double)Styles.Dictionary["DvLine13"];
        underBox.Child = under;
        underBox.Padding = Tokens.Space("DvSpace3", "DvSpace2");

        forget.Click += async (_, _) => await model.ForgetNote();
        note.Margin = new(0, Tokens.Number("DvSpace2"), 0, 0);
        note.Children.Add(forget);
        note.Children.Add(noteLine);
        note.Children.Add(forgetProblem);

        card.Children.Add(framed);
        card.Children.Add(underBox);
        card.Children.Add(note);
    }

    private void BuildAsk()
    {
        askTitle.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightHeading"];
        AutomationProperties.SetHeadingLevel(askTitle, AutomationHeadingLevel.Level2);
        var head = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        head.Children.Add(askTitle);
        head.Children.Add(askState);

        approve.Click += async (_, _) => await model.Answer(true);
        deny.Click += async (_, _) => await model.Answer(false);
        var actions = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        actions.Children.Add(approve);
        actions.Children.Add(deny);

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        column.Children.Add(head);
        column.Children.Add(askDetails);
        column.Children.Add(actions);
        column.Children.Add(problem);

        ask.Cut = Tokens.Number("DvCut");
        ask.Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace3"), Tokens.Number("DvSpace4"), Tokens.Number("DvSpace4"));
        ask.Children.Add(column);
        AutomationProperties.SetLabeledBy(ask, askTitle);
    }

    #endregion

    #region Drawing

    private void Apply()
    {
        if (Card is not { } shown)
        {
            return;
        }

        (model.Card, model.BotName, model.Decide, model.Forget, model.Forgotten, model.MemoryHref) = (shown, BotName, Decide, Forget, Forgotten, MemoryHref);
        model.ParametersSet();
    }

    private void Draw()
    {
        if (Card is null || model.Card is null)
        {
            card.Visibility = ask.Visibility = Visibility.Collapsed;
            return;
        }

        ask.Visibility = model.IsAsk ? Visibility.Visible : Visibility.Collapsed;
        card.Visibility = model.IsAsk ? Visibility.Collapsed : Visibility.Visible;

        if (model.IsAsk)
        {
            DrawAsk();
        }
        else
        {
            DrawCard();
        }

        drawn = model.Card;
    }

    private void DrawAsk()
    {
        ask.Edge = Tokens.Brush(this, "DvWarn");
        askTitle.Text = model.AskTitle;
        askState.Text = model.Card.State;
        if (!ReferenceEquals(drawn, model.Card))
        {
            askDetails.Content = AskDetails();
        }

        // Each answer is described by the question, so it is never just "Approve" out of context.
        AutomationProperties.SetName(approve, ToolCallCardViewModel.Approve);
        AutomationProperties.SetFullDescription(approve, model.AskTitle);
        AutomationProperties.SetName(deny, ToolCallCardViewModel.Deny);
        AutomationProperties.SetFullDescription(deny, model.AskTitle);
        // The answers are offered only once the question says what they answer.
        approve.IsEnabled = deny.IsEnabled = model.CanAnswer;
        problem.Text = model.Problem;
    }

    private StackPanel? AskDetails()
    {
        if (model.ShowsFacts)
        {
            return Pieces.Facts(model.Card.Facts);
        }

        var details = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        if (model.Summary is { } summary)
        {
            var line = Styles.Text(summary, "DvTextMono");
            line.Foreground = Tokens.Brush(this, "DvText");
            details.Children.Add(line);
        }

        if (model.AskShowsArguments)
        {
            details.Children.Add(Code(model.Card.Arguments, ToolCallCardViewModel.ArgumentsTitle));
        }

        return details.Children.Count is 0 ? null : details;
    }

    private void DrawCard()
    {
        section.Title = model.Card.Label;
        section.Summary = model.Summary;
        section.Running = model.Running;
        section.Open = model.Open;
        state.Text = model.Card.State;
        state.Kind = model.StateKind;

        endedEdge.Visibility = model.EndedEdge is null ? Visibility.Collapsed : Visibility.Visible;
        endedEdge.Background = model.EndedEdge is { } edge ? Tokens.Brush(this, edge) : null;

        if (model.Open && (!ReferenceEquals(drawn, model.Card) || !drawnOpen))
        {
            DrawBody();
        }

        drawnOpen = model.Open;
        DrawUnder();
        DrawNote();
    }

    private void DrawBody()
    {
        body.Children.Clear();

        if (model.ShowsFacts)
        {
            body.Children.Add(Pieces.Facts(model.Card.Facts));
        }
        else if (model.ShowsArguments)
        {
            body.Children.Add(Pieces.Part(ToolCallCardViewModel.ArgumentsTitle, Code(model.Card.Arguments)));
        }

        if (model.ShowsOutput)
        {
            // The one box is kept while output arrives, so what a person scrolled to or selected stays put.
            (output.Parent as Panel)?.Children.Remove(output);
            output.Show(null, null, model.Card.Output);
            body.Children.Add(Pieces.Part(ToolCallCardViewModel.OutputTitle, output));
        }

        if (model.ShowsResult)
        {
            var result = Pieces.Part(ToolCallCardViewModel.ResultTitle);
            if (model.ExitLine is { } exit)
            {
                result.Children.Add(Pieces.Quiet(exit));
            }

            if (model.Card.Result is { } text)
            {
                result.Children.Add(Code(text));
            }

            body.Children.Add(result);
        }

        if (model.ShowsQuiet)
        {
            body.Children.Add(Pieces.Quiet(ToolCallCardViewModel.Quiet));
        }
    }

    // Under the card: why a command could not run, or the first line it printed while the card is closed.
    private void DrawUnder()
    {
        var (text, isError) = model.ErrorLine is { } error ? (error, true) : (model.PreviewLine, false);
        underBox.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        under.Text = text ?? string.Empty;
        under.FontFamily = isError ? Tokens.FontUi : Tokens.FontMono;
        under.Foreground = Tokens.Brush(this, isError ? "DvDanger" : "DvText2");
        underBox.Background = isError ? null : Tokens.Brush(this, "DvBg2");
        AutomationProperties.SetName(under, isError || text is null ? string.Empty : $"{ToolCallCardViewModel.PreviewName}: {text}");
    }

    // Under a "Remembered" card: the one step that takes the note back, then the line that says it is gone.
    private void DrawNote()
    {
        var offered = model.Note;
        note.Visibility = offered is ToolNoteState.None ? Visibility.Collapsed : Visibility.Visible;
        forget.Visibility = offered is ToolNoteState.Offered ? Visibility.Visible : Visibility.Collapsed;
        noteLine.Visibility = offered is ToolNoteState.Forgotten or ToolNoteState.ChangedSince ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(forget, model.ForgetName);
        forgetProblem.Text = model.ForgetProblem;

        if (offered is ToolNoteState.Forgotten)
        {
            noteLine.Say(MemoryWords.Forgotten);
        }
        else if (offered is ToolNoteState.ChangedSince)
        {
            noteLine.Say(model.ChangedSinceLine);
            if (model.MemoryHref is { } memory)
            {
                noteLine.Words.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " " });
                noteLine.Words.Inlines.Add(Pieces.LinkIn(this, ToolCallCardViewModel.OpenMemory, memory));
            }
        }

        if (model.TakeFocusForLine())
        {
            noteLine.TakeFocus();
        }
    }

    // Arguments and results wrap, so a phone shows all of a line without scrolling sideways. Long ones scroll down.
    private static CodeBlock Code(string text, string? language = null) => new() { Code = text, CodeLanguage = language, Wraps = true };

    #endregion
}
