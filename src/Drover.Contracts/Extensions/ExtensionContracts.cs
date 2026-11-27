namespace Drover.Contracts.Extensions;

/// <summary>An extension as the text of its file, exactly as the person chose or pasted it. Sent to read the file, and sent again to import it.</summary>
public sealed record ExtensionText(string Package);

/// <summary>
/// What an extension holds, for a person to read before anything is added and to see afterwards. Nothing outside
/// the server has been contacted to make it.
/// </summary>
public sealed record ExtensionParts
{
    #region Properties

    /// <summary>The options a person fills in for the extension, as the file declares them.</summary>
    public IReadOnlyList<ExtensionSetting> Settings { get; init; } = [];

    /// <summary>The themes it offers the space's members.</summary>
    public IReadOnlyList<ExtensionTheme> Themes { get; init; } = [];

    /// <summary>The rules it offers for a bot. A person adds one to a bot; the extension adds none.</summary>
    public IReadOnlyList<BotRuleBody> Rules { get; init; } = [];

    #endregion
}

/// <summary>A theme an extension offers, by the words a person chooses it by.</summary>
public sealed record ExtensionTheme(string Id, string Name, string Intent);

/// <summary>What an extension would add. <see cref="AlreadyInstalled"/> says an extension with this id is in the space already.</summary>
public sealed record ExtensionPreview(string PackageId, string Name, string Description)
{
    #region Properties

    public ExtensionConnectionPreview? Connection { get; init; }

    public ExtensionParts Parts { get; init; } = new();

    public bool AlreadyInstalled { get; init; }

    #endregion
}

/// <summary>
/// The tool server an extension names. <see cref="Host"/> is where calls would go, shown large. <see cref="Auth"/> is
/// <c>none</c>, <c>header</c>, or <c>oauth</c>. <see cref="DependsOnSettings"/> says the address holds a setting,
/// so where calls go is known only once the settings are filled in. <see cref="IsLocal"/> says the address is on the
/// server's own machine or network, or is not https: connecting to it needs the person's explicit yes.
/// <see cref="AsksForHeader"/> says the person who connects types the header's whole value.
/// <see cref="Available"/> is false where this server has tool connections turned off.
/// </summary>
public sealed record ExtensionConnectionPreview(string Name, string Url, string Host, string Auth, string? HeaderName, bool IsLocal, bool Available)
{
    #region Properties

    public bool DependsOnSettings { get; init; }

    public bool AsksForHeader { get; init; }

    public IReadOnlyList<SuggestedTool> SuggestedTools { get; init; } = [];

    #endregion
}

/// <summary>
/// Connects the tool server an installed extension names. The address is the file's, with the settings filled in.
/// <see cref="Settings"/> holds a value for each setting the file declares, by its key; a secret one goes to the
/// vault and is never returned. <see cref="AcceptLocalAddress"/> is the person's yes to an address on the server's
/// own network or one that is not https. <see cref="HeaderValue"/> is the header's value where the file leaves it to
/// the person: it goes to the vault and is never returned.
/// </summary>
public sealed record ConnectExtensionRequest(bool AcceptLocalAddress = false, string? HeaderValue = null)
{
    #region Properties

    public IReadOnlyDictionary<string, string>? Settings { get; init; }

    #endregion

    #region Public Methods

    public override string ToString() => nameof(ConnectExtensionRequest);

    #endregion
}

/// <summary>
/// The tool server of an installed extension and how far its setup has come. <see cref="ConnectionId"/> is null
/// until a person connects it, and again after the connection is removed. <see cref="Status"/> is the connection's
/// status word, and <see cref="Problem"/> what its last listing said went wrong. <see cref="Tools"/> are the
/// suggested tools, each with the catalog's tool once the server lists it.
/// </summary>
public sealed record ExtensionConnectionSummary(string Name, string Url, string Host, string Auth, string? HeaderName, bool IsLocal, bool Available)
{
    #region Properties

    public bool DependsOnSettings { get; init; }

    public bool AsksForHeader { get; init; }

    public Guid? ConnectionId { get; init; }

    public string? Status { get; init; }

    public string? Problem { get; init; }

    public IReadOnlyList<SuggestedToolState> Tools { get; init; } = [];

    #endregion
}

/// <summary>A suggested tool on an installed extension. <see cref="ToolId"/> is null while the server does not list a tool by that name.</summary>
public sealed record SuggestedToolState(string Name, string Why, Guid? ToolId);

/// <summary>
/// An extension installed in the space. <see cref="FilledSettings"/> are the values a person gave its settings, by
/// key; a secret is never among them.
/// </summary>
public sealed record ExtensionSummary(Guid Id, string PackageId, string Name, string Description, DateTimeOffset InstalledAt)
{
    #region Properties

    /// <summary>The tool server the extension names, when it names one.</summary>
    public ExtensionConnectionSummary? Connection { get; init; }

    public ExtensionParts Parts { get; init; } = new();

    public IReadOnlyDictionary<string, string> FilledSettings { get; init; } = new Dictionary<string, string>();

    #endregion
}

/// <summary>
/// A theme the space offers its members, from an installed extension. <see cref="File"/> is the theme file's text;
/// the device checks it before offering it. <see cref="Source"/> is the extension's name.
/// </summary>
public sealed record SpaceTheme(string Id, string Name, string Source, string File);
