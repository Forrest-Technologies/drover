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

    public string Get(string key, string fallback)
    {
        if (IsolatedPath() is { } path)
        {
            return Read(path).TryGetValue(key, out var value) ? value : fallback;
        }

        return Settings.Values.TryGetValue(key, out var stored) && stored is string text ? text : fallback;
    }

    public void Set(string key, string value)
    {
        if (IsolatedPath() is { } path)
        {
            var stored = Read(path);
            stored[key] = value;
            Write(path, stored);
            return;
        }

        Settings.Values[key] = value;
    }

    public bool Get(string key, bool fallback)
    {
        if (IsolatedPath() is { } path)
        {
            return Read(path).TryGetValue(key, out var value) ? value == "true" : fallback;
        }

        return Settings.Values.TryGetValue(key, out var stored) && stored is bool flag ? flag : fallback;
    }

    public void Set(string key, bool value)
    {
        if (IsolatedPath() is { } path)
        {
            var stored = Read(path);
            stored[key] = value ? "true" : "false";
            Write(path, stored);
            return;
        }

        Settings.Values[key] = value;
    }

    public void Remove(string key)
    {
        if (IsolatedPath() is { } path)
        {
            var stored = Read(path);
            stored.Remove(key);
            Write(path, stored);
            return;
        }

        Settings.Values.Remove(key);
    }

    #endregion

    #region Private Methods

    // A screen-check run keeps its server choice beside its report. The regular app's settings stay untouched.
    private static string? IsolatedPath()
    {
        var report = Environment.GetEnvironmentVariable("DROVER_UI_CHECK_REPORT");
        if (string.IsNullOrWhiteSpace(report))
        {
            return null;
        }

        var directory = System.IO.Path.GetDirectoryName(report);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        return System.IO.Path.Combine(directory, "checks-preferences.txt");
    }

    private static Dictionary<string, string> Read(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var stored = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            var split = line.IndexOf('=');
            if (split > 0)
            {
                stored[line[..split]] = line[(split + 1)..];
            }
        }

        return stored;
    }

    private static void Write(string path, Dictionary<string, string> stored)
    {
        var lines = stored.Select(pair => $"{pair.Key}={pair.Value}");
        File.WriteAllLines(path, lines);
    }

    #endregion
}
