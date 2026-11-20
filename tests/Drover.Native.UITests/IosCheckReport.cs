namespace Drover.Native.UITests;

/// <summary>
/// The report file the iOS app writes inside its simulator container. The simulator shows that container as a
/// folder on this machine, so the harness reads it here. The app's console is not a reliable place to wait:
/// <c>simctl launch --console</c> returns when the launch handoff ends, and the app is still starting.
/// </summary>
internal static class IosCheckReport
{
    #region Private Fields

    public const string ResultPrefix = "ui-check result:";

    #endregion

    #region Public Methods

    public static string File(string container) =>
        Path.Combine(container, "Documents", "ui-check.txt");

    public static IReadOnlyList<string> Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return
        [
            .. reader.ReadToEnd()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];
    }

    /// <summary>The result line, or null when the app has not finished.</summary>
    public static string? Result(IReadOnlyList<string> lines) =>
        lines.LastOrDefault(line => line.StartsWith(ResultPrefix, StringComparison.Ordinal));

    #endregion
}
