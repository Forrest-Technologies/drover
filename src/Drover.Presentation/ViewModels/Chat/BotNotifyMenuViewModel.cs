namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// The "More" menu about a bot: whatever the screen puts in it, and whether this bot may notify the person. The
/// choice is read once; until it is known the menu offers nothing about it.
/// </summary>
public sealed partial class BotNotifyMenuViewModel(
    DroverApi api,
    SessionState session,
    ToastService toasts,
    ILogger<BotNotifyMenuViewModel> logger) : ObservableObject
{
    #region Private Fields

    private bool loaded;

    private bool asking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotifyLabel), nameof(HasMenu), nameof(AccessibleLabel))]
    private BotSummary? bot;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotifyLabel), nameof(HasMenu))]
    private bool? notifies;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMenu))]
    private bool hasItems;

    #endregion

    #region Properties

    public static string Label => "More";

    /// <summary>The menu button's fuller name.</summary>
    public string AccessibleLabel => $"More about {Bot?.Name}";

    /// <summary>The menu is there when the screen put something in it, or the choice about notifying is known.</summary>
    public bool HasMenu => HasItems || Notifies is not null;

    /// <summary>The item that turns notifying on or off, once the choice is known.</summary>
    public string? NotifyLabel => (Notifies, Bot) switch
    {
        (true, { } shown) => $"Stop notifying me about {shown.Name}",
        (false, { } shown) => $"Notify me about {shown.Name}",
        _ => null,
    };

    #endregion

    #region Public Methods

    /// <summary>Reads the choice for the bot, once. A failure leaves the item out; the next time the menu is shown asks again.</summary>
    public async Task Show(BotSummary shown)
    {
        Bot = shown;

        if (loaded || asking || session.Space is not { } space)
        {
            return;
        }

        asking = true;

        try
        {
            Notifies = (await api.GetBotNotify(space.Id, shown.Id)).Notifies;
            loaded = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotNotifyMenuViewModel));
        }
        finally
        {
            asking = false;
        }
    }

    [RelayCommand]
    public Task Flip() => Notifies is { } on ? Set(!on) : Task.CompletedTask;

    public async Task Set(bool on)
    {
        if (session.Space is not { } space || Bot is not { } shown)
        {
            return;
        }

        try
        {
            Notifies = (await api.SetBotNotify(space.Id, shown.Id, on)).Notifies;
            toasts.Show(
                on ? $"{shown.Name} can notify you on devices where notifications are on." : $"{shown.Name} will not notify you.",
                ToastKind.Success);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotNotifyMenuViewModel));
            toasts.Show($"The choice was not saved. {Failures.Sentence(exception)}", ToastKind.Error);
        }
    }

    #endregion
}
