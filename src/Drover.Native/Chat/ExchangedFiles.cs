namespace Drover.Native.Chat;

/// <summary>
/// STAND-IN: replaced at merge by the real control.
/// It has the surface of the web client's <c>ExchangedFiles</c> and shows only its own name, so the screen that hosts it builds.
/// </summary>
public sealed class ExchangedFiles : Grid
{
    #region Constructors

    public ExchangedFiles()
    {
        Children.Add(Styles.Text(nameof(ExchangedFiles), "DvTextCaption"));
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<ExchangedFiles, BotSummary?>(nameof(Bot), null);

    public static readonly DependencyProperty VersionProperty = Dp.Register<ExchangedFiles, int>(nameof(Version), 0);

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
