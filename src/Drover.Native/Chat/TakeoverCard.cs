namespace Drover.Native.Chat;

/// <summary>
/// A bot asks a person to take over its screen and waits. Three moments, one card: the ask, the person holding the
/// screen, and the answer given. Nothing moves while it waits: waiting is not a live state.
/// </summary>
public sealed class TakeoverCard : Grid
{
    #region Private Fields

    private readonly TakeoverCardViewModel model = new();

    private readonly Batch batch;

    private readonly Facet frame = new();

    private readonly TextBlock title = Styles.Text(null);

    private readonly Badge state = new() { Wraps = true };

    private readonly TextBlock reason = Styles.Text(null);

    private readonly TextBlock note = Pieces.Quiet(null);

    private readonly FlowPanel actions = new() { Gap = Tokens.Number("DvSpace2") };

    private readonly StackPanel decline = new() { Spacing = Tokens.Number("DvSpace2") };

    private readonly TextArea declineNote = new() { Rows = 2 };

    private readonly Button tell = new();

    private readonly Button back = new() { Content = TakeoverCardViewModel.Back };

    private readonly StatusNote problem = new() { Kind = NoteKind.Danger };

    private readonly List<Button> buttons = [];

    private IReadOnlyList<TakeoverAction> drawnActions = [];

    #endregion

    #region Constructors

    public TakeoverCard()
    {
        batch = new(this, Apply, Draw);

        title.FontWeight = (Windows.UI.Text.FontWeight)Tokens.Dictionary["DvWeightHeading"];
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level3);
        var head = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        head.Children.Add(title);
        head.Children.Add(state);

        declineNote.RegisterPropertyChangedCallback(TextArea.ValueProperty, (_, _) => model.Note = declineNote.Value);
        tell.Click += (_, _) => model.Decline();
        back.Click += (_, _) => model.GoBack();
        var declineActions = new FlowPanel { Gap = Tokens.Number("DvSpace2") };
        declineActions.Children.Add(tell);
        declineActions.Children.Add(back);
        decline.Children.Add(declineNote);
        decline.Children.Add(declineActions);

        var column = new StackPanel { Spacing = Tokens.Number("DvSpace3") };
        column.Children.Add(head);
        column.Children.Add(reason);
        column.Children.Add(note);
        column.Children.Add(actions);
        column.Children.Add(decline);
        column.Children.Add(problem);

        frame.Cut = Tokens.Number("DvCut");
        frame.Padding = new(Tokens.Number("DvSpace4"), Tokens.Number("DvSpace3"), Tokens.Number("DvSpace4"), Tokens.Number("DvSpace4"));
        frame.Children.Add(column);
        AutomationProperties.SetLabeledBy(frame, title);
        Children.Add(frame);

        model.OpenScreen += () => OpenScreen?.Invoke(this, EventArgs.Empty);
        model.Done += () => Done?.Invoke(this, EventArgs.Empty);
        model.CouldNot += words => CouldNot?.Invoke(this, words);
        model.PropertyChanged += (_, _) => batch.Draw();
        this.WhenShownOrRethemed(batch.Draw);
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotNameProperty = Dp.Register<TakeoverCard, string>(nameof(BotName), string.Empty, card => card.batch.Values());

    public static readonly DependencyProperty ReasonProperty = Dp.Register<TakeoverCard, string>(nameof(Reason), string.Empty, card => card.batch.Values());

    public static readonly DependencyProperty PhaseProperty =
        Dp.Register<TakeoverCard, TakeoverPhase>(nameof(Phase), TakeoverPhase.Asked, card => card.batch.Values());

    public static readonly DependencyProperty SendingProperty = Dp.Register<TakeoverCard, bool>(nameof(Sending), false, card => card.batch.Values());

    public static readonly DependencyProperty ProblemProperty = Dp.Register<TakeoverCard, string?>(nameof(Problem), null, card => card.batch.Values());

    public static readonly DependencyProperty AccessProperty =
        Dp.Register<TakeoverCard, ScreenAccess>(nameof(Access), ScreenAccess.Usable, card => card.batch.Values());

    public static readonly DependencyProperty SetupHrefProperty = Dp.Register<TakeoverCard, string?>(nameof(SetupHref), null, card => card.batch.Values());

    public static readonly DependencyProperty AnswerBesideScreenProperty =
        Dp.Register<TakeoverCard, bool>(nameof(AnswerBesideScreen), false, card => card.batch.Values());

    public static readonly DependencyProperty TakeFocusProperty = Dp.Register<TakeoverCard, bool>(nameof(TakeFocus), false, card => card.batch.Values());

    /// <summary>The bot that asks. Required.</summary>
    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    /// <summary>Why the bot needs a person, in the bot's own short words. Required.</summary>
    public string Reason
    {
        get => (string)GetValue(ReasonProperty);
        set => SetValue(ReasonProperty, value);
    }

    public TakeoverPhase Phase
    {
        get => (TakeoverPhase)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>True while an answer is on its way to the server.</summary>
    public bool Sending
    {
        get => (bool)GetValue(SendingProperty);
        set => SetValue(SendingProperty, value);
    }

    /// <summary>Why the answer could not be sent, as a sentence.</summary>
    public string? Problem
    {
        get => (string?)GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }

    /// <summary>Whether this person can open the bot's screen. Until it is known the card offers no answer.</summary>
    public ScreenAccess Access
    {
        get => (ScreenAccess)GetValue(AccessProperty);
        set => SetValue(AccessProperty, value);
    }

    /// <summary>Where the bot's screen is set up, for a bot that has none now.</summary>
    public string? SetupHref
    {
        get => (string?)GetValue(SetupHrefProperty);
        set => SetValue(SetupHrefProperty, value);
    }

    /// <summary>The screen is open beside the conversation with its own copy of the controls, so this one only points there.</summary>
    public bool AnswerBesideScreen
    {
        get => (bool)GetValue(AnswerBesideScreenProperty);
        set => SetValue(AnswerBesideScreenProperty, value);
    }

    /// <summary>"I'm done" takes the keyboard focus when the card turns to holding, as the control that replaced "Open the screen".</summary>
    public bool TakeFocus
    {
        get => (bool)GetValue(TakeFocusProperty);
        set => SetValue(TakeFocusProperty, value);
    }

    public event EventHandler? OpenScreen;

    public event EventHandler? Done;

    /// <summary>The person could not do it. The text is what they chose to tell the bot, and may be empty.</summary>
    public event EventHandler<string>? CouldNot;

    #endregion

    #region Private Methods

    private void Apply()
    {
        (model.BotName, model.Reason, model.Phase, model.Sending, model.Problem) = (BotName, Reason, Phase, Sending, Problem);
        (model.Access, model.SetupHref, model.AnswerBesideScreen, model.TakeFocus) = (Access, SetupHref, AnswerBesideScreen, TakeFocus);
        model.ParametersSet();
    }

    private void Draw()
    {
        frame.Edge = model.Edge is { } edge ? Tokens.Brush(this, edge) : null;
        title.Text = model.Title;
        AutomationProperties.SetName(frame, model.Title);
        state.Text = model.State;
        state.Kind = model.StateKind;
        reason.Text = model.Reason;
        DrawNote();
        DrawActions();

        decline.Visibility = model.ShowsDecline ? Visibility.Visible : Visibility.Collapsed;
        declineNote.Label = model.DeclineLabel;
        if (declineNote.Value != model.Note)
        {
            declineNote.Value = model.Note;
        }

        tell.Content = model.DeclineSubmit;
        tell.IsEnabled = back.IsEnabled = !model.Sending;
        problem.Text = model.Problem;
    }

    private void DrawNote()
    {
        var words = model.NoteWords;
        note.Visibility = words.Count is 0 ? Visibility.Collapsed : Visibility.Visible;
        note.Inlines.Clear();
        foreach (var part in words)
        {
            note.Inlines.Add(part.Href is { } address
                ? Pieces.LinkIn(this, part.Text, address)
                : new Microsoft.UI.Xaml.Documents.Run { Text = part.Text });
        }

        // While the card is still finding out whether the person can use the screen, the line is a status.
        AutomationProperties.SetLiveSetting(note, model.NoteIsStatus ? AutomationLiveSetting.Polite : AutomationLiveSetting.Off);
        if (model.NoteIsStatus)
        {
            FrameworkElementAutomationPeer.FromElement(note)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    // The buttons are kept while the same ones are offered, so the one being pressed keeps the focus.
    private void DrawActions()
    {
        var offered = model.Actions;
        if (!offered.SequenceEqual(drawnActions))
        {
            drawnActions = offered;
            actions.Children.Clear();
            buttons.Clear();
            foreach (var action in offered)
            {
                var button = new Button { Content = action.Label, Kind = action.Primary ? ButtonKind.Primary : ButtonKind.Quiet };
                button.Click += (_, _) => model.Press(action.Step);
                buttons.Add(button);
                actions.Children.Add(button);
            }
        }

        actions.Visibility = offered.Count is 0 ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in buttons)
        {
            // Each answer is described by the title, so it is never just "It is done" out of context.
            AutomationProperties.SetFullDescription(button, model.Title);
            button.IsEnabled = !model.Sending;
        }

        if (model.TakeFocusForDone() && buttons.FirstOrDefault(button => button.Kind is ButtonKind.Primary) is { } done)
        {
            Pieces.FocusWhenShown(done, done.TakeFocus);
        }
    }

    #endregion
}
