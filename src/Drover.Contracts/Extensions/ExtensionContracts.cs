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

    public ExtensionConnectionPreview? Connection { get; init; }

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
/// The tool server an extension names. <see cref="Host"/> is where calls would go, shown large. <see cref="Auth"/> is
/// <c>none</c>, <c>header</c>, or <c>oauth</c>. <see cref="IsLocal"/> says the address is on the server's own machine
/// or network, or is not https: connecting to it needs the person's explicit yes. <see cref="Available"/> is false
/// where this server has tool connections turned off, and then the extension's bot is all that can be added.
/// </summary>
public sealed record ExtensionConnectionPreview(string Name, string Url, string Host, string Auth, string? HeaderName, bool IsLocal, bool Available)
{
    #region Properties

    public IReadOnlyList<SuggestedTool> SuggestedTools { get; init; } = [];

    #endregion
}

/// <summary>
/// Connects the tool server an installed extension names. The address is the file's and cannot be given here.
/// <see cref="AcceptLocalAddress"/> is the person's yes to an address on the server's own network or one that is not
/// https. <see cref="HeaderValue"/> is the secret a header connection sends: it goes to the vault and is never returned.
/// </summary>
public sealed record ConnectExtensionRequest(bool AcceptLocalAddress = false, string? HeaderValue = null);

/// <summary>
/// The tool server of an installed extension and how far its setup has come. <see cref="ConnectionId"/> is null
/// until a person connects it, and again after the connection is removed. <see cref="Status"/> is the connection's
/// status word, and <see cref="Problem"/> what its last listing said went wrong. <see cref="Tools"/> are the
/// suggested tools, each with the catalog's tool once the server lists it.
/// </summary>
public sealed record ExtensionConnectionSummary(string Name, string Url, string Host, string Auth, string? HeaderName, bool IsLocal, bool Available)
{
    #region Properties

    public Guid? ConnectionId { get; init; }

    public string? Status { get; init; }

    public string? Problem { get; init; }

    public IReadOnlyList<SuggestedToolState> Tools { get; init; } = [];

    #endregion
}

/// <summary>
/// A suggested tool on an installed extension. <see cref="ToolId"/> is null while the server does not list a tool
/// by that name. <see cref="Grant"/> is the bot's own grant for it: <c>off</c>, <c>ask</c>, or <c>allow</c>.
/// </summary>
public sealed record SuggestedToolState(string Name, string Why, Guid? ToolId, string Grant);

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

    /// <summary>The tool server the extension names, when it names one.</summary>
    public ExtensionConnectionSummary? Connection { get; init; }

    #endregion
}
