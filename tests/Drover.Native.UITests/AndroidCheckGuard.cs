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

    /// <summary>Report lines written at or after <paramref name="startedEpochSeconds"/>, device time.</summary>
    public static IReadOnlyList<string> ReportsSince(string log, long startedEpochSeconds)
    {
        List<string> lines = [];
        foreach (var raw in log.Split('\n'))
        {
            var trimmed = raw.Trim();
            var split = trimmed.IndexOf(' ');
            if (split <= 0)
            {
                continue;
            }

            if (!double.TryParse(
                trimmed[..split],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var stamp))
            {
                continue;
            }

            if (stamp + 1 < startedEpochSeconds)
            {
                continue;
            }

            const string Marker = "ui-check";
            var at = trimmed.IndexOf(Marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                lines.Add(trimmed[at..]);
            }
        }

        return lines;
    }

    #endregion
}
