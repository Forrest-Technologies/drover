namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>TakeoverCall</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class TakeoverCall : Grid
{
    #region Constructors

    public TakeoverCall()
    {
        Children.Add(Styles.Text(nameof(TakeoverCall), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty TakeoverProperty = Dp.Register<TakeoverCall, Takeover?>(nameof(Takeover), null);

    public static readonly DependencyProperty BotNameProperty = Dp.Register<TakeoverCall, string>(nameof(BotName), string.Empty);

    public static readonly DependencyProperty TakeoversProperty = Dp.Register<TakeoverCall, ScreenTakeovers?>(nameof(Takeovers), null);

    public Takeover? Takeover
    {
        get => (Takeover?)GetValue(TakeoverProperty);
        set => SetValue(TakeoverProperty, value);
    }

    public string BotName
    {
        get => (string)GetValue(BotNameProperty);
        set => SetValue(BotNameProperty, value);
    }

    public ScreenTakeovers? Takeovers
    {
        get => (ScreenTakeovers?)GetValue(TakeoversProperty);
        set => SetValue(TakeoversProperty, value);
    }

    #endregion
}
