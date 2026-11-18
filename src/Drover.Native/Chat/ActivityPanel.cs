namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>ActivityPanel</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class ActivityPanel : Grid
{
    #region Constructors

    public ActivityPanel()
    {
        Children.Add(Styles.Text(nameof(ActivityPanel), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<ActivityPanel, BotSummary?>(nameof(Bot), null);

    public static readonly DependencyProperty BotsProperty = Dp.Register<ActivityPanel, IReadOnlyList<BotSummary>>(nameof(Bots), []);

    public static readonly DependencyProperty RunIdProperty = Dp.Register<ActivityPanel, Guid?>(nameof(RunId), null);

    public static readonly DependencyProperty VersionProperty = Dp.Register<ActivityPanel, int>(nameof(Version), 0);

    public BotSummary? Bot
    {
        get => (BotSummary?)GetValue(BotProperty);
        set => SetValue(BotProperty, value);
    }

    public IReadOnlyList<BotSummary> Bots
    {
        get => (IReadOnlyList<BotSummary>)GetValue(BotsProperty);
        set => SetValue(BotsProperty, value);
    }

    public Guid? RunId
    {
        get => (Guid?)GetValue(RunIdProperty);
        set => SetValue(RunIdProperty, value);
    }

    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    #endregion
}
