namespace Drover.Native.UITests;

/// <summary>
/// Decides whether the harness may wipe an installed app. The run names the device, and the package on it must be
/// the checks build. A device that was not named keeps its data, emulator or not.
/// </summary>
internal static class AndroidCheckGuard
{
    #region Private Fields

    public const string ChecksVersion = "checks";

    #endregion

    #region Public Methods

    public static IReadOnlyList<string> Serials(string devicesText) =>
    [
        ..devicesText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("List of devices", StringComparison.Ordinal) && !line.StartsWith('*'))
            .Select(line => line.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries)[0]),
    ];

    public static bool IsEmulator(string serial) =>
        serial.StartsWith("emulator-", StringComparison.Ordinal);

    public static string? VersionName(string packageDump)
    {
        foreach (var line in packageDump.Split('\n'))
        {
            var trimmed = line.Trim();
            const string Prefix = "versionName=";
            if (trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return trimmed[Prefix.Length..];
            }
        }

        return null;
    }

    /// <summary>
    /// The device to wipe. The caller names the device, and its installed package must be the checks build. The
    /// first emulator is not assumed to be disposable.
    /// </summary>
    public static string? Choose(IReadOnlyList<string> serials, string? selected, string? versionName)
    {
        if (string.IsNullOrWhiteSpace(selected) || versionName != ChecksVersion)
        {
            return null;
        }

        return serials.Contains(selected) ? selected : null;
    }

    /// <summary>Report lines written under <paramref name="tag"/> for <paramref name="runId"/>.</summary>
    public static IReadOnlyList<string> ReportsForRun(string log, string runId, string tag = "DroverUiCheck")
    {
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(tag))
        {
            return [];
        }

        List<string> lines = [];
        var prefix = $"{runId} ui-check";
        foreach (var line in TagLogsForRun(log, runId, tag))
        {
            var at = line.IndexOf(prefix, StringComparison.Ordinal);
            if (at >= 0)
            {
                lines.Add(line[(at + runId.Length + 1)..]);
            }
        }

        return lines;
    }

    /// <summary>Lines written under <paramref name="tag"/> for <paramref name="runId"/>.</summary>
    public static IReadOnlyList<string> TagLogsForRun(string log, string runId, string tag)
    {
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(tag))
        {
            return [];
        }

        List<string> lines = [];
        var tagMarker = $"{tag}:";
        var runMarker = $"{runId} ";
        foreach (var raw in log.Split('\n'))
        {
            var trimmed = raw.Trim();
            var tagAt = trimmed.IndexOf(tagMarker, StringComparison.Ordinal);
            if (tagAt < 0)
            {
                continue;
            }

            var runAt = trimmed.IndexOf(runMarker, tagAt + tagMarker.Length, StringComparison.Ordinal);
            if (runAt >= 0 && char.IsWhiteSpace(trimmed[runAt - 1]))
            {
                lines.Add(trimmed);
            }
        }

        return lines;
    }

    #endregion
}
