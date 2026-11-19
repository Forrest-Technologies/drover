namespace Drover.Native.UITests;

/// <summary>
/// Decides whether the harness may wipe an installed app. A phone keeps its data. An emulator is disposable, and
/// so is a package built for these checks.
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

    /// <summary>The device to wipe, or null when wiping would touch something that is not disposable.</summary>
    public static string? Choose(IReadOnlyList<string> serials, string? versionName)
    {
        var emulator = serials.FirstOrDefault(IsEmulator);
        if (emulator is not null)
        {
            return emulator;
        }

        return serials.Count == 1 && versionName == ChecksVersion ? serials[0] : null;
    }

    #endregion
}
