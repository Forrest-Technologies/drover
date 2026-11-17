namespace Drover.Domain.Files;

/// <summary>
/// What a bot can do with files in one conversation: whether its model reads pictures, whether it has a sandbox to put
/// a file in, and whether everyone who can reach that sandbox can see the conversation. A bot's sandbox is reachable
/// beyond one conversation (by the space's admins, and by the bot's runs for other people), so a file is copied into
/// it only when the conversation is shared with the whole space, or the space is personal and has one member.
/// </summary>
public sealed record FileAbilities(bool ModelReadsImages, bool HasSandbox, bool AudienceCoversSandbox);

/// <summary>How a file reaches a bot.</summary>
public enum FileUse
{
    /// <summary>The model is given the picture itself.</summary>
    Image,

    /// <summary>The model is given the text, up to a bound.</summary>
    Text,

    /// <summary>The file is copied into the bot's sandbox and the model is told where.</summary>
    Sandbox,

    /// <summary>The bot has no way to open it. The composer says so before the message is sent.</summary>
    None,
}

public static class FileUses
{
    #region Public Methods

    /// <summary>
    /// A picture goes to a model that reads pictures, text goes in as text, and anything else, a picture the model
    /// cannot read included, goes to the bot's sandbox when it has one and everyone who can reach it can see the conversation.
    /// </summary>
    public static FileUse For(FileKind kind, FileAbilities abilities) => kind switch
    {
        FileKind.Image when abilities.ModelReadsImages => FileUse.Image,
        FileKind.Text => FileUse.Text,
        _ when abilities is { HasSandbox: true, AudienceCoversSandbox: true } => FileUse.Sandbox,
        _ => FileUse.None,
    };

    /// <summary>Why a bot cannot open a file of this kind, as a refusal code, or null when it can.</summary>
    public static string? RefusalFor(FileKind kind, FileAbilities abilities) =>
        For(kind, abilities) is not FileUse.None ? null
        : abilities.HasSandbox ? FileRefusals.PrivateConversation
        : kind is FileKind.Image ? FileRefusals.ModelCannotReadImages
        : FileRefusals.NothingCanOpenIt;

    #endregion
}
