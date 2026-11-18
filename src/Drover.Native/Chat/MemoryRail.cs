namespace Drover.Native.Chat;

/// <summary>The notes a bot keeps about the person reading, read from the server and changed through it.</summary>
public sealed class MemoryRail : Grid
{
    #region Private Fields

    private readonly MemoryRailViewModel model;

    private readonly Batch batch;

    private readonly MemoryPanel panel = new();

    #endregion

    #region Constructors

    public MemoryRail()
        : this(Pieces.Make<MemoryRailViewModel>())
    {
    }

    /// <summary>A rail with a view model made elsewhere, for the gallery, which reads from a sample server.</summary>
    internal MemoryRail(MemoryRailViewModel model)
    {
        this.model = model;
        batch = new(this, Apply, Draw);
        (panel.OnAdd, panel.OnChange) = (model.Add, model.Change);
        panel.Remove += async (_, noteId) => await model.Remove(noteId);
        panel.Retry += async (_, _) => await model.Load();
        Children.Add(panel);

        // Undo is chosen in the toast, which then goes, so the focus moves to the note it put back.
        model.Restored += noteId => DispatcherQueue.TryEnqueue(() =>
        {
            batch.Flush();
            panel.FocusNote(noteId);
        });
        model.PropertyChanged += (_, _) => batch.Draw();
    }

    #endregion

    #region Properties

    public static readonly DependencyProperty BotProperty = Dp.Register<MemoryRail, BotSummary?>(nameof(Bot), null, rail => rail.batch.Values());

    public static readonly DependencyProperty VersionProperty = Dp.Register<MemoryRail, int>(nameof(Version), 0, rail => rail.batch.Values());

    /// <summary>The bot whose notes are shown. Required.</summary>
    public BotSummary? Bot
    {
        get => (BotSummary?)GetValue(BotProperty);
        set => SetValue(BotProperty, value);
    }

    /// <summary>Changes when the bot has stopped working, which is when it may have changed its notes.</summary>
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
        if (Bot is not { } bot || model.Bot is null)
        {
            return;
        }

        (panel.State, panel.BotName, panel.Notes, panel.NearlyFull) = (model.State, bot.Name, model.Notes, model.NearlyFull);
        (panel.Problem, panel.EditorHref) = (model.Problem, model.EditorHref);
    }

    #endregion
}
