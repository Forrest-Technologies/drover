namespace Drover.Presentation.ViewModels.Chat;

/// <summary>The chat status bar preference and recent model choices for this app session.</summary>
public sealed class ConversationDisplaySettings(IDeviceSettings device, ILogger<ConversationDisplaySettings> logger)
    : ObservableObject
{
    #region Private Fields

    public const string StatusBarKey = "drover.conversation.status-line";

    public const string StatusBarLabel = "Show conversation status bar";

    private const int RecentLimit = 5;

    private bool? showStatusBar;

    private readonly Dictionary<(Guid User, Guid Connection), IReadOnlyList<string>> recent = [];

    #endregion

    #region Properties

    public bool ShowStatusBar
    {
        get => showStatusBar ??= LoadStatusBar();
        set
        {
            if (value != ShowStatusBar)
            {
                showStatusBar = value;
                try
                {
                    device.Write(StatusBarKey, value ? "on" : "off");
                }
                catch (InvalidOperationException exception)
                {
                    logger.LogWarning(exception, "The conversation status bar preference could not be saved");
                }

                OnPropertyChanged();
            }
        }
    }

    #endregion

    #region Public Methods

    public IReadOnlyList<string> Recent(Guid user, Guid connection) => recent.GetValueOrDefault((user, connection)) ?? [];

    public void Remember(Guid user, Guid connection, string model) =>
        recent[(user, connection)] = [model, .. Recent(user, connection).Where(value => value != model).Take(RecentLimit - 1)];

    #endregion

    #region Private Methods

    private bool LoadStatusBar()
    {
        try
        {
            return device.Read(StatusBarKey) is not "off";
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "The conversation status bar preference could not be read");
            return true;
        }
    }

    #endregion
}
