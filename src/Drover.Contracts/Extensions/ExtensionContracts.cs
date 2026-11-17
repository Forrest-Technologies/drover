namespace Drover.Contracts.Extensions;

/// <summary>
/// An extension as the text of its file, exactly as the person chose or pasted it. Sent to read the file, and sent
/// again to import it: the bot it adds starts paused and does nothing until a person turns it on.
/// </summary>
public sealed record ExtensionText(string Package);

/// <summary>
/// What an extension would add, for a person to read before anything is added. Nothing outside the server has been
/// contacted to make it. <see cref="AlreadyInstalled"/> says an extension with this id is in the space already.
/// </summary>
public sealed record ExtensionPreview(string PackageId, string Name, string Description)
{
    #region Properties

    public ExtensionBotPreview? Bot { get; init; }

    public bool AlreadyInstalled { get; init; }

    #endregion
}

/// <summary>
/// The bot an extension would add. <see cref="Instructions"/> is the whole text the bot would be given, shown in
/// full and as plain text. When <see cref="HiddenCharacters"/> is above zero the text holds characters that do not
/// show on a screen, and each is written out where it stands, as <c>[U+200B]</c>, so nothing in it is unseen.
/// <see cref="Model"/> is the model the bot would think with and <see cref="ModelConnection"/> the name of the
/// connection it is on: the model the file suggests when that connection lists it, and the connection's own
/// otherwise. Both are null when the space has no model the bot could use, and then nothing can be imported.
/// </summary>
public sealed record ExtensionBotPreview(string Name, string Description, string Instructions, int HiddenCharacters)
{
    #region Properties

    public string? Model { get; init; }

    public string? ModelConnection { get; init; }

    #endregion
}


/// <summary>
/// An extension installed in the space, with the bot it added. The bot is null once it was removed.
/// <see cref="BotStatus"/> is the bot's status word; a bot that was never turned on is <c>paused</c>.
/// </summary>
public sealed record ExtensionSummary(Guid Id, string PackageId, string Name, string Description, DateTimeOffset InstalledAt)
{
    #region Properties

    public Guid? BotId { get; init; }

    public string? BotName { get; init; }

    public string? BotStatus { get; init; }

    #endregion
}
