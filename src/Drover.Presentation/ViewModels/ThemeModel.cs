namespace Drover.Presentation.ViewModels;

/// <summary>
/// The theme choice: which theme, in which mode, with which accent. The app is the default theme, dark, until a
/// person picks otherwise, and the choice is kept on the device under the name the web app always used. A value
/// written before a person could choose a theme reads as the default theme in the mode it names.
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
    /// The theme the choice names, with the accent it names. The default theme when the app has no theme by that
    /// name, as a device that never chose does, and the theme's own color when it does not offer that accent.
    /// </summary>
    public ThemeFile Theme => Chosen.WithAccent(Choice.Accent);

    /// <summary>The theme the choice names as its file holds it, in its own color.</summary>
    public ThemeFile Chosen => BuiltInThemes.Get(Choice.ThemeId) ?? BuiltInThemes.Default;

    /// <summary>True when the choice is the black mode, which the dark mode of the window is then drawn in.</summary>
    public bool IsBlack => Choice.Mode is ThemeMode.Black;

    #endregion

    #region Public Methods

    public void Load() => Choice = ThemeChoice.Parse(Stored(), logger);

    /// <summary>Takes another mode of the theme in use.</summary>
    public void Choose(ThemeMode mode) => Choose(Choice with { Mode = mode });

    /// <summary>Takes another accent for the theme in use.</summary>
    public void ChooseAccent(string accentId) => Choose(Choice with { Accent = accentId });

    /// <summary>Takes another theme, in the mode in use and in the theme's own color.</summary>
    public void ChooseTheme(string themeId) => Choose(Choice with { ThemeId = themeId, Accent = Accents.ThemeId });

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
            // Storage can be turned off. The app is then the default theme, dark.
            logger.LogWarning(exception, "The theme choice could not be read from this device");
            return null;
        }
    }

    #endregion
}
