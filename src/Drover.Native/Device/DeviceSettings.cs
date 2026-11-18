namespace Drover.Native.Platform;

/// <summary>Small choices kept in the platform's own settings store. Never a secret.</summary>
internal sealed class DeviceSettings : IDeviceSettings
{
    #region Properties

    private static Windows.Storage.ApplicationDataContainer Settings => Windows.Storage.ApplicationData.Current.LocalSettings;

    #endregion

    #region Interface Implementations

    public string? Read(string name) => Settings.Values.TryGetValue(name, out var value) ? value as string : null;

    public void Write(string name, string value) => Settings.Values[name] = value;

    public void Remove(string name) => Settings.Values.Remove(name);

    #endregion
}
