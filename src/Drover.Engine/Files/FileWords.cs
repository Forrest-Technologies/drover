namespace Drover.Engine.Files;

/// <summary>Sizes and file refusals in a person's words, for the sentences the server answers with.</summary>
public static class FileWords
{
    #region Public Methods

    /// <summary>"512 bytes", "40 KB", "3.4 MB", "25 MB".</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{Rounded(bytes / 1024d)} KB",
        < 1024L * 1024 * 1024 => $"{Rounded(bytes / (1024d * 1024))} MB",
        _ => $"{Rounded(bytes / (1024d * 1024 * 1024))} GB",
    };

    public static string TooLarge(long? bytes, long maxBytes) =>
        bytes is { } size
            ? $"This file is {Size(size)}. This server takes files up to {Size(maxBytes)}."
            : $"This file is larger than {Size(maxBytes)}, the most this server takes.";

    public static string SpaceFull(int maxMegabytes) =>
        $"This space keeps at most {maxMegabytes} MB of files, and this one would take it past that.";

    public static string UploadExpired(TimeSpan lifetime) =>
        $"This upload took longer than {(int)Math.Round(lifetime.TotalMinutes)} minutes and expired before it finished.";

    public static string TooMany(int maxFiles) => $"A message carries at most {maxFiles} files on this server.";

    public static string CannotOpen(string refusal, string botName) => refusal switch
    {
        FileRefusals.ModelCannotReadImages => $"{botName}'s model cannot read pictures, and {botName} has no sandbox to put this one in.",
        FileRefusals.PrivateConversation => $"This conversation is private, and others in this space can reach {botName}'s sandbox, so this file cannot go there.",
        _ => $"{botName} has no way to open this kind of file. It needs a sandbox for it.",
    };

    #endregion

    #region Private Methods

    private static string Rounded(double value) =>
        value < 10 ? value.ToString("0.#", CultureInfo.InvariantCulture) : Math.Round(value).ToString(CultureInfo.InvariantCulture);

    #endregion
}
