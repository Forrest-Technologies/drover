#if !__ANDROID__
namespace Drover.Native.Platform;

/// <summary>
/// Keeps screen-check settings beside the run's report so regular desktop settings stay untouched. Compiled only
/// into a build with <c>DroverUiChecks=true</c>.
/// </summary>
public sealed partial class Preferences
{
    #region Private Methods

    static partial void TryGetIsolatedText(string key, string fallback, ref bool handled, ref string value)
    {
        if (IsolatedPath() is not { } path)
        {
            return;
        }

        handled = true;
        value = Read(path).TryGetValue(key, out var stored) ? stored : fallback;
    }

    static partial void TrySetIsolatedText(string key, string value, ref bool handled)
    {
        if (IsolatedPath() is not { } path)
        {
            return;
        }

        handled = true;
        var stored = Read(path);
        stored[key] = value;
        Write(path, stored);
    }

    static partial void TryGetIsolatedFlag(string key, bool fallback, ref bool handled, ref bool value)
    {
        if (IsolatedPath() is not { } path)
        {
            return;
        }

        handled = true;
        value = Read(path).TryGetValue(key, out var stored) ? stored == "true" : fallback;
    }

    static partial void TrySetIsolatedFlag(string key, bool value, ref bool handled)
    {
        if (IsolatedPath() is not { } path)
        {
            return;
        }

        handled = true;
        var stored = Read(path);
        stored[key] = value ? "true" : "false";
        Write(path, stored);
    }

    static partial void TryRemoveIsolated(string key, ref bool handled)
    {
        if (IsolatedPath() is not { } path)
        {
            return;
        }

        handled = true;
        var stored = Read(path);
        stored.Remove(key);
        Write(path, stored);
    }

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
#endif
