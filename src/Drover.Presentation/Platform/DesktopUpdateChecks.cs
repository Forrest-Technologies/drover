namespace Drover.Presentation.Platform;

/// <summary>The on-device switch for signed desktop update checks.</summary>
public static class DesktopUpdateChecks
{
    #region Public Methods

    public const string SettingName = "drover-update-checks";

    public static bool AreEnabled(IDeviceSettings settings) => settings.Read(SettingName) is not "off";

    public static void Store(IDeviceSettings settings, bool enabled)
    {
        if (enabled)
        {
            settings.Remove(SettingName);
        }
        else
        {
            settings.Write(SettingName, "off");
        }
    }

    #endregion
}
