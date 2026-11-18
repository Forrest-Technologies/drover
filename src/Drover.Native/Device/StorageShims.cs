namespace Drover.Native.Platform;

/// <summary>
/// The secret store the shared session code asks for. It has the shape of the one the Android shell is given, so
/// the same source compiles in both apps.
/// </summary>
public interface ISecureStorage
{
    // The names are the ones of the Android shell's store, which the shared session code calls. They cannot
    // change here without forking that code.
#pragma warning disable DRV0001
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);
#pragma warning restore DRV0001

    bool Remove(string key);
}

/// <summary>
/// The plain settings the shared server-address code asks for, with the shape of the one the Android shell uses.
/// Only the server's address is kept here, never a secret.
/// </summary>
public sealed class Preferences
{
    #region Properties

    public static Preferences Default { get; } = new();

    private static Windows.Storage.ApplicationDataContainer Settings => Windows.Storage.ApplicationData.Current.LocalSettings;

    #endregion

    #region Public Methods

    public string Get(string key, string fallback) => Settings.Values.TryGetValue(key, out var value) && value is string text ? text : fallback;

    public void Set(string key, string value) => Settings.Values[key] = value;

    public bool Get(string key, bool fallback) => Settings.Values.TryGetValue(key, out var value) && value is bool flag ? flag : fallback;

    public void Set(string key, bool value) => Settings.Values[key] = value;

    public void Remove(string key) => Settings.Values.Remove(key);

    #endregion
}
