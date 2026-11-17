namespace Drover.Presentation.ViewModels;

/// <summary>
/// The theme choice. The app is dark until a person picks light or asks it to follow the device, and the choice is
/// kept on the device under the name the web app always used.
/// </summary>
public sealed partial class ThemeModel(IDeviceSettings settings, ILogger<ThemeModel> logger) : ObservableObject
{
    #region Private Fields

    /// <summary>The name of the stored choice. In a browser it is the same local storage entry the web app wrote.</summary>
    public const string SettingName = "drover.theme";

    [ObservableProperty]
    private ThemeChoice choice = ThemeChoice.Dark;

    #endregion

    #region Public Methods

    public void Load() => Choice = Enum.TryParse<ThemeChoice>(Stored(), ignoreCase: true, out var stored)
        ? stored
        : ThemeChoice.Dark;

    public void Choose(ThemeChoice chosen)
    {
        Choice = chosen;

        try
        {
            settings.Write(SettingName, chosen.ToString().ToLowerInvariant());
        }
        catch (InvalidOperationException exception)
        {
            // Storage can be turned off. The choice still holds for this visit.
            logger.LogWarning(exception, "The theme choice could not be kept on this device");
        }
    }

    #endregion

    #region Private Methods

    private string? Stored()
    {
        try
        {
            return settings.Read(SettingName);
        }
        catch (InvalidOperationException exception)
        {
            // Storage can be turned off. The app is then dark.
            logger.LogWarning(exception, "The theme choice could not be read from this device");
            return null;
        }
    }

    #endregion
}
