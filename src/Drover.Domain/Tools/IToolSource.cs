namespace Drover.Domain.Tools;

/// <summary>
/// Lists and calls the tools of one tool server. One implementation per kind of source. Everything it returns from
/// a server (names, descriptions, schemas, results) is text chosen by a third party: the caller stores and shows it
/// and hands results to a model as tool output, and never treats any of it as an instruction.
/// </summary>
public interface IToolSource
{
    /// <summary>The kind of tool this source serves.</summary>
    ToolSourceKind Kind { get; }

    /// <summary>The server's tools as it defines them now, and what was negotiated to get them.</summary>
    Task<ToolListing> ListTools(ToolEndpoint endpoint, CancellationToken cancellationToken);

    /// <summary>
    /// Calls one tool by the name the server gave it. A tool that ran and reported an error is a result with
    /// <see cref="ToolOutcome.IsError"/> set. A server that could not be reached, timed out, refused the credential,
    /// or does not speak the required protocol is a <see cref="ToolSourceException"/>.
    /// </summary>
    Task<ToolOutcome> CallTool(ToolEndpoint endpoint, string toolName, JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>
/// Where a tool server is and how to reach it. <see cref="Headers"/> carries the credential, read from the vault by
/// the caller for this one use. A source adds it at the transport and nowhere else: never to a log, an error
/// message, or anything it returns. <see cref="CredentialKey"/> names the credential without revealing it, so a
/// source that keeps a session open keeps one per credential and never shares it between people.
/// </summary>
public sealed record ToolEndpoint(Guid ConnectionId, Uri Url)
{
    #region Properties

    public ToolTransport Transport { get; init; } = ToolTransport.StreamableHttp;

    public ProtocolMode ProtocolMode { get; init; } = ProtocolMode.Automatic;

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public Guid? CredentialKey { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    public ToolServerLimits Limits { get; init; } = ToolServerLimits.Default;

    #endregion
}

/// <summary>
/// How much Drover accepts from a tool server. A hostile or broken server can answer with an endless body, an
/// endless chain of pages, or millions of tools, so each is capped; the deployment policy sets the values.
/// </summary>
public sealed record ToolServerLimits(int MaxResponseBytes, int MaxTools)
{
    /// <summary>The self-hosted values, used where a caller states none.</summary>
    public static ToolServerLimits Default { get; } = new(8 * 1024 * 1024, 500);
}

/// <summary>
/// A server's tool list. <see cref="ProtocolVersion"/> and <see cref="IsStateless"/> say what was negotiated, for the
/// badge on the connection. <see cref="TimeToLive"/> is how long the server says the list may be reused, if it says.
/// </summary>
public sealed record ToolListing(IReadOnlyList<ListedTool> Tools, string ProtocolVersion, bool IsStateless, TimeSpan? TimeToLive = null);

/// <summary>
/// One tool as its server defines it. The hints are the server's own claims about the tool; they set a starting
/// sensitivity that a person can raise, and are never trusted to lower one.
/// </summary>
public sealed record ListedTool(string Name, string? Title, string Description, JsonElement InputSchema)
{
    #region Properties

    public JsonElement? Annotations { get; init; }

    public bool ClaimsReadOnly { get; init; }

    public bool ClaimsDestructive { get; init; }

    #endregion
}

public enum ToolSourceFailure
{
    Unreachable,
    TimedOut,
    Unauthorized,
    ProtocolNotSupported,
    Blocked,
    ServerError,
    TooLarge,
}

/// <summary>A tool server could not be used. The message is plain words for a person and holds no credential.</summary>
public sealed class ToolSourceException(ToolSourceFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    #region Properties

    public ToolSourceFailure Failure { get; } = failure;

    #endregion
}
