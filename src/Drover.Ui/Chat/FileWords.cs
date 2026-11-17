namespace Drover.Ui.Chat;

/// <summary>How a file is shown: as a picture, as its first lines, or as a card to download.</summary>
public enum FileKind
{
    Image,
    Text,
    Other,
}

/// <summary>Where a file being attached stands.</summary>
public enum UploadPhase
{
    Uploading,
    Ready,
    Failed,
}

/// <summary>Why a file cannot go with a message. Said before sending, never after.</summary>
public enum FileRefusal
{
    None,
    TooLarge,
    TooMany,
    ModelCannotReadImages,
    NothingCanOpenIt,
    SpaceFull,
    PrivateConversation,
}

/// <summary>A file chosen for a message that has not been sent yet.</summary>
public sealed record AttachmentItem(
    Guid Id,
    string Name,
    long Size,
    UploadPhase Phase,
    int Percent,
    FileRefusal Refusal = FileRefusal.None);

/// <summary>
/// A file in a conversation, from the person or from the bot. <see cref="Width"/> and <see cref="Height"/> are set
/// for a picture, so nothing jumps while it loads. <see cref="Preview"/> is a text file's first lines.
/// </summary>
public sealed record SharedFile(
    Guid Id,
    string Name,
    string ContentType,
    long Size,
    string Href,
    bool FromBot,
    DateTimeOffset When,
    string? MomentHref = null,
    string? Caption = null,
    string? Preview = null,
    int? Width = null,
    int? Height = null);

/// <summary>Files in words: what kind a file is, how big, who it came from, and why one cannot be sent.</summary>
public static class FileWords
{
    #region Private Fields

    private static readonly Dictionary<string, string> kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = "PDF document",
        ["application/zip"] = "Zip archive",
        ["application/json"] = "JSON file",
        ["text/csv"] = "CSV table",
        ["text/markdown"] = "Markdown file",
        ["text/html"] = "HTML file",
        ["text/plain"] = "Text file",
    };

    #endregion

    #region Public Methods

    public static FileKind Kind(SharedFile file) => file switch
    {
        { Width: not null, Height: not null }
            when file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => FileKind.Image,
        { Preview: not null } => FileKind.Text,
        _ => FileKind.Other,
    };

    /// <summary>The kind in a person's words: "PDF document", "PNG image", "Text file", or the extension.</summary>
    public static string KindName(string contentType, string name)
    {
        if (kinds.TryGetValue(contentType, out var known))
        {
            return known;
        }

        var dot = name.LastIndexOf('.');
        var extension = dot > 0 ? name[(dot + 1)..].ToUpperInvariant() : string.Empty;
        return (contentType.Split('/')[0].ToLowerInvariant(), extension) switch
        {
            ("image", { Length: > 0 }) => $"{extension} image",
            ("image", _) => "Image",
            ("audio", _) => "Audio file",
            ("video", _) => "Video file",
            ("text", _) => "Text file",
            (_, { Length: > 0 }) => $"{extension} file",
            _ => "File",
        };
    }

    /// <summary>The most characters of a text file "Show all" puts on the page.</summary>
    public const int MaxShownCharacters = 100_000;

    /// <summary>
    /// True when a text file holds more than the first lines it shows. Each shown line may have lost a carriage return
    /// and the last its line break, so a file only that much larger has nothing more.
    /// </summary>
    public static bool HasMore(SharedFile file) =>
        file.Preview is { } preview && file.Size > Encoding.UTF8.GetByteCount(preview) + preview.Count(character => character == '\n') + 2;

    /// <summary>What "Show all" says when the file is longer than it shows.</summary>
    public static string Cut(string name) =>
        $"Only the first {MaxShownCharacters.ToString("N0", CultureInfo.InvariantCulture)} characters of {name} are shown. Download it for the rest.";

    /// <summary>A card's second line: "PDF document, 3.4 MB".</summary>
    public static string Facts(SharedFile file) => $"{KindName(file.ContentType, file.Name)}, {Words.Size(file.Size)}";

    public static string From(SharedFile file, string botName) => file.FromBot ? $"From {botName}" : "From you";

    /// <summary>What a chip says under the file's name while it is being attached.</summary>
    public static string Status(AttachmentItem item, string botName) => item switch
    {
        { Refusal: not FileRefusal.None } => Refused(item.Refusal, botName),
        { Phase: UploadPhase.Uploading, Percent: <= 0 } => "Uploading",
        { Phase: UploadPhase.Uploading } => $"Uploading, {Math.Clamp(item.Percent, 0, 100)} percent",
        { Phase: UploadPhase.Failed } => "Not uploaded. Remove it and attach it again.",
        _ => Words.Size(item.Size),
    };

    public static string Refused(FileRefusal refusal, string botName) => refusal switch
    {
        FileRefusal.TooLarge => "Larger than this server takes.",
        FileRefusal.TooMany => "More files than one message may carry.",
        FileRefusal.ModelCannotReadImages => $"{botName}'s model cannot read pictures.",
        FileRefusal.NothingCanOpenIt => $"{botName} has no way to open this kind of file. It needs a sandbox for it.",
        FileRefusal.SpaceFull => "This space has no room left for files on this server.",
        FileRefusal.PrivateConversation => $"This conversation is private, and others in this space can reach {botName}'s sandbox, so this file cannot go there.",
        _ => string.Empty,
    };

    /// <summary>
    /// The refusal a server's refusal code names. A code this screen does not know reads as a failed upload.
    /// </summary>
    public static FileRefusal RefusalFrom(string? code) => code switch
    {
        FileRefusals.TooLarge => FileRefusal.TooLarge,
        FileRefusals.TooMany => FileRefusal.TooMany,
        FileRefusals.SpaceFull => FileRefusal.SpaceFull,
        FileRefusals.ModelCannotReadImages => FileRefusal.ModelCannotReadImages,
        FileRefusals.NothingCanOpenIt => FileRefusal.NothingCanOpenIt,
        FileRefusals.PrivateConversation => FileRefusal.PrivateConversation,
        _ => FileRefusal.None,
    };

    /// <summary>
    /// A file a message carries, as the conversation shows it. <paramref name="href"/> is where the server serves
    /// it. The card does not show when it was shared, so the moment is left at its default here.
    /// </summary>
    public static SharedFile Shared(FileResponse file, string href, bool fromBot) =>
        new(
            file.Id,
            file.Name,
            file.ContentType,
            file.Size,
            href,
            fromBot,
            default,
            Caption: file.Caption,
            Preview: file.Preview,
            Width: file.Width,
            Height: file.Height);

    /// <summary>A file a person and a bot exchanged, as the files panel lists it: when, and a link to that moment.</summary>
    public static SharedFile Exchanged(ExchangedFileResponse exchanged, string href, string momentHref) =>
        Shared(exchanged.File, href, exchanged.FromBot) with { When = exchanged.SharedAt, MomentHref = momentHref };

    /// <summary>True when the message can be sent: every file is uploaded and none is refused.</summary>
    public static bool CanSend(IEnumerable<AttachmentItem> items) =>
        items.All(item => item is { Phase: UploadPhase.Ready, Refusal: FileRefusal.None });

    #endregion
}
