namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>TakeoverCard</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class TakeoverCard : Grid
{
    #region Constructors

    public TakeoverCard()
    {
        Children.Add(Styles.Text(nameof(TakeoverCard), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotNameProperty = Dp.Register<TakeoverCard, string>(nameof(BotName), string.Empty);

    public static readonly DependencyProperty ReasonProperty = Dp.Register<TakeoverCard, string>(nameof(Reason), string.Empty);

    public static readonly DependencyProperty PhaseProperty = Dp.Register<TakeoverCard, TakeoverPhase>(nameof(Phase), default(TakeoverPhase));

    public static readonly DependencyProperty SendingProperty = Dp.Register<TakeoverCard, bool>(nameof(Sending), false);

    public static readonly DependencyProperty ProblemProperty = Dp.Register<TakeoverCard, string?>(nameof(Problem), null);

    public static readonly DependencyProperty AccessProperty = Dp.Register<TakeoverCard, ScreenAccess>(nameof(Access), ScreenAccess.Usable);

    public static readonly DependencyProperty SetupHrefProperty = Dp.Register<TakeoverCard, string?>(nameof(SetupHref), null);

    public static readonly DependencyProperty AnswerBesideScreenProperty = Dp.Register<TakeoverCard, bool>(nameof(AnswerBesideScreen), false);

    public static readonly DependencyProperty TakeFocusProperty = Dp.Register<TakeoverCard, bool>(nameof(TakeFocus), false);

    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

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

    public bool Sending
    {
        get => (bool)GetValue(SendingProperty);
        set => SetValue(SendingProperty, value);
    }

    public string? Problem
    {
        get => (string?)GetValue(ProblemProperty);
        set => SetValue(ProblemProperty, value);
    }

    public ScreenAccess Access
    {
        get => (ScreenAccess)GetValue(AccessProperty);
        set => SetValue(AccessProperty, value);
    }

    public string? SetupHref
    {
        get => (string?)GetValue(SetupHrefProperty);
        set => SetValue(SetupHrefProperty, value);
    }

    public bool AnswerBesideScreen
    {
        get => (bool)GetValue(AnswerBesideScreenProperty);
        set => SetValue(AnswerBesideScreenProperty, value);
    }

    public bool TakeFocus
    {
        get => (bool)GetValue(TakeFocusProperty);
        set => SetValue(TakeFocusProperty, value);
    }

#pragma warning disable CS0067 // The stand-in never raises it; the real control does.
    public event EventHandler? OnOpenScreen;
#pragma warning restore CS0067

#pragma warning disable CS0067 // The stand-in never raises it; the real control does.
    public event EventHandler? OnDone;
#pragma warning restore CS0067

#pragma warning disable CS0067 // The stand-in never raises it; the real control does.
    public event EventHandler<string>? OnCouldNot;
#pragma warning restore CS0067

    #endregion
}
