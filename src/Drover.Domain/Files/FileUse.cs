namespace Drover.Domain.Files;

/// <summary>
/// What a bot can do with files in one conversation: whether its model reads pictures, whether it has a sandbox to put
/// a file in, whether everyone who can reach that sandbox can see the conversation, and whether its model reads a PDF
/// as a PDF. A bot's sandbox is reachable
/// beyond one conversation (by the space's admins, and by the bot's runs for other people), so a file is copied into
/// it only when the conversation is shared with the whole space, or the space is personal and has one member.
/// </summary>
public sealed record FileAbilities(bool ModelReadsImages, bool HasSandbox, bool AudienceCoversSandbox, bool ModelReadsPdf = false);

/// <summary>How a file reaches a bot.</summary>
public enum FileUse
{
    /// <summary>The model is given the picture itself.</summary>
    Image,

    /// <summary>The model is given the text, up to a bound.</summary>
    Text,

    /// <summary>
    /// The server reads the document's text and the model is given it, up to a bound; a scanned PDF's pages go to a
    /// model that reads pictures as pictures.
    /// </summary>
    Document,

    /// <summary>
    /// The model is given the PDF itself, because its model reads PDFs. One past the server's or the provider's bound
    /// is given as a <see cref="Document"/> instead.
    /// </summary>
    NativeDocument,

    /// <summary>The file is copied into the bot's sandbox and the model is told where.</summary>
    Sandbox,

    /// <summary>The bot has no way to open it. The composer says so before the message is sent.</summary>
    None,
}

public static class FileUses
{
    #region Public Methods

    /// <summary>
    /// A picture goes to a model that reads pictures, text goes in as text, a document's text is read out for any
    /// bot, and anything else, a picture the model cannot read included, goes to the bot's sandbox when it has one and
    /// everyone who can reach it can see the conversation.
    /// </summary>
    public static FileUse For(FileKind kind, FileAbilities abilities) => kind switch
    {
        FileKind.Image when abilities.ModelReadsImages => FileUse.Image,
        FileKind.Text => FileUse.Text,
        FileKind.Document => FileUse.Document,
        _ when abilities is { HasSandbox: true, AudienceCoversSandbox: true } => FileUse.Sandbox,
        _ => FileUse.None,
    };

    /// <summary>Why a bot cannot open a file of this kind, as a refusal code, or null when it can.</summary>
    public static string? RefusalFor(FileKind kind, FileAbilities abilities) =>
        For(kind, abilities) is not FileUse.None ? null
        : abilities.HasSandbox ? FileRefusals.PrivateConversation
        : kind is FileKind.Image ? FileRefusals.ModelCannotReadImages
        : FileRefusals.NothingCanOpenIt;

    /// <summary>
    /// As <see cref="For(FileKind, FileAbilities)"/> for a file of this content type, except that a PDF goes to a model
    /// that reads PDFs as itself.
    /// </summary>
    public static FileUse For(string contentType, FileAbilities abilities) =>
        contentType is DocumentTypes.Pdf && abilities.ModelReadsPdf
            ? FileUse.NativeDocument
            : For(FileInspector.KindOf(contentType), abilities);

    #endregion
}
