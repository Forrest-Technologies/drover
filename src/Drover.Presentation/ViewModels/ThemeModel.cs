namespace Drover.Presentation.ViewModels;

/// <summary>
/// The theme choice: which theme, and in which mode. The app is Night Yard, dark, until a person picks otherwise,
/// and the choice is kept on the device under the name the web app always used. A value an older version wrote, one
/// word for the mode, reads as Night Yard in that mode.
/// </summary>
public sealed partial class ThemeModel(IDeviceSettings settings, ILogger<ThemeModel> logger) : ObservableObject
{
    #region Private Fields

    /// <summary>The name of the stored choice. In a browser it is the same local storage entry the web app wrote.</summary>
    public const string SettingName = "drover.theme";

    [ObservableProperty]
    private ThemeChoice choice = ThemeChoice.Default;

    #endregion

    #region Properties

    /// <summary>
    /// The theme the choice names. Night Yard when the app has no theme by that name, as a device that never chose
    /// does.
    /// </summary>
    public ThemeFile Theme => BuiltInThemes.Get(Choice.ThemeId) ?? BuiltInThemes.NightYard;

    #endregion

    #region Public Methods

    public void Load() => Choice = ThemeChoice.Parse(Stored(), logger);

    /// <summary>Takes another mode of the theme in use.</summary>
    public void Choose(ThemeMode mode) => Choose(Choice with { Mode = mode });

    public void Choose(ThemeChoice chosen)
    {
        Choice = chosen;

        try
        {
            settings.Write(SettingName, chosen.ToStored());
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
            // Storage can be turned off. The app is then Night Yard, dark.
            logger.LogWarning(exception, "The theme choice could not be read from this device");
            return null;
        }
    }

    #endregion
}
