namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>ScreenPanel</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class ScreenPanel : Grid
{
    #region Constructors

    public ScreenPanel()
    {
        Children.Add(Styles.Text(nameof(ScreenPanel), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<ScreenPanel, BotSummary?>(nameof(Bot), null);

    public static readonly DependencyProperty TakeoverProperty = Dp.Register<ScreenPanel, Takeover?>(nameof(Takeover), null);

    public BotSummary? Bot
    {
        get => (BotSummary?)GetValue(BotProperty);
        set => SetValue(BotProperty, value);
    }

    public Takeover? Takeover
    {
        get => (Takeover?)GetValue(TakeoverProperty);
        set => SetValue(TakeoverProperty, value);
    }

#pragma warning disable CS0067 // The stand-in never raises it; the real control does.
    public event EventHandler<Guid>? OnHandedBack;
#pragma warning restore CS0067

    #endregion
}
