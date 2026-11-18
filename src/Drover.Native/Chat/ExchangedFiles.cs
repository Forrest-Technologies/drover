namespace Drover.Native.Chat;

/// <summary>The files a person and a bot exchanged, read from the server, as the rail beside the conversation shows them.</summary>
public sealed class ExchangedFiles : Grid
{
    #region Private Fields

    private readonly ExchangedFilesViewModel model;

    private readonly Batch batch;

    private readonly FilesPanel panel = new();

    #endregion

    #region Constructors

    public ExchangedFiles()
        : this(Pieces.Make<ExchangedFilesViewModel>())
    {
    }

    /// <summary>A rail with a view model made elsewhere, for the gallery, which reads from a sample server.</summary>
    internal ExchangedFiles(ExchangedFilesViewModel model)
    {
        this.model = model;
        batch = new(this, Apply, Draw);
        panel.ReadWhole = model.ReadWhole;
        panel.Retry += async (_, _) => await model.Load();
        Children.Add(panel);
        model.PropertyChanged += (_, _) => batch.Draw();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<ExchangedFiles, BotSummary?>(nameof(Bot), null, files => files.batch.Values());

    public static readonly DependencyProperty VersionProperty = Dp.Register<ExchangedFiles, int>(nameof(Version), 0, files => files.batch.Values());

    /// <summary>The bot the files were exchanged with. Required.</summary>
    public BotSummary? Bot
    {
        get => (BotSummary?)GetValue(BotProperty);
        set => SetValue(BotProperty, value);
    }

    /// <summary>Changes when a conversation with the bot has moved on.</summary>
    public int Version
    {
        get => (int)GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    #endregion

    #region Private Methods

    private async void Apply()
    {
        if (Bot is not { } bot)
        {
            return;
        }

        (model.Bot, model.Version) = (bot, Version);
        await model.ParametersSet();
    }

    private void Draw()
    {
        if (Bot is not { } bot)
        {
            return;
        }

        (panel.State, panel.BotName, panel.Files, panel.Problem) = (model.State, bot.Name, model.Files, model.Problem);
    }

    #endregion
}
