namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>SummaryPanel</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class SummaryPanel : Grid
{
    #region Constructors

    public SummaryPanel()
    {
        Children.Add(Styles.Text(nameof(SummaryPanel), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<SummaryPanel, BotSummary?>(nameof(Bot), null);

    public static readonly DependencyProperty VersionProperty = Dp.Register<SummaryPanel, int>(nameof(Version), 0);

    public BotSummary? Bot
    {
        get => (BotSummary?)GetValue(BotProperty);
        set => SetValue(BotProperty, value);
    }

    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    #endregion
}
