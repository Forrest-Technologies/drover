namespace Drover.Ui.Theme;

/// <summary>Turns a theme choice into what the server keeps for a person's devices, and back.</summary>
public static class AppearanceSettings
{
    #region Public Methods

    public static AppearanceSetting ToSetting(this ThemeChoice choice) =>
        new(choice.ThemeId, choice.Mode.ToString().ToLowerInvariant());

    /// <summary>The choice a kept setting names, or null when it is not one this version can read.</summary>
    public static ThemeChoice? ToChoice(this AppearanceSetting setting) =>
        setting.ThemeId.Length > 0
        && Enum.TryParse<ThemeMode>(setting.Mode, ignoreCase: true, out var mode)
        && Enum.IsDefined(mode)
            ? new(setting.ThemeId, mode)
            : null;

    #endregion
}
