namespace Drover.Contracts.Tools;

/// <summary>
/// A tool connection as the space sees it. <see cref="AuthKind"/> is none, header, or oauth;
/// <see cref="Trust"/> is trusted or untrusted; <see cref="ProtocolMode"/> is automatic, stateless_only, or
/// session_only; <see cref="Status"/> is pending, ready, failed, or disabled. <see cref="NegotiatedProtocol"/> is
/// what the server and Drover settled on, for a badge: it starts with "stateless" or "session-based".
/// <see cref="Slug"/> prefixes the names a model sees for this connection's tools. A credential is never returned:
/// a header connection has one, and that is all a response says.
/// For a connection people sign in to (<c>oauth</c>), <see cref="SignInScope"/> is per_user (each person signs
/// in themself) or space (an admin signs in once for everyone), and <see cref="SignIn"/> says how the sign-in the
/// caller would use stands: signed_in, needs_sign_in, or needs_reconnect. It is null for other connections.
/// </summary>
public sealed record ToolConnectionSummary(
    Guid Id,
    string Name,
    string Slug,
    string Url,
    string AuthKind,
    string Trust,
    string ProtocolMode,
    string Status,
    string? NegotiatedProtocol,
    DateTimeOffset? LastListedAt,
    string? LastError)
{
    #region Properties

    public string SignInScope { get; init; } = "space";

    public string? SignIn { get; init; }

    /// <summary>The sign-in service the connection is pinned to, when an admin entered a client id. Not a secret.</summary>
    public string? Issuer { get; init; }

    #endregion
}

/// <summary>
/// One tool a connection lists. <see cref="Name"/> is the server's name for it and <see cref="ModelName"/> the name
/// a model sees. <see cref="Sensitivity"/> is read, write, or critical; <see cref="State"/> is available or missing.
/// The description is the server's own text: show it as text, never as markup or instructions.
/// <see cref="InputSchema"/> is the tool's input schema as JSON text. When <see cref="NeedsReview"/> is set, the
/// server changed a tool somebody was granted: the tool is offered to no bot until an admin confirms the change,
/// and <see cref="Reviewed"/> is the definition as it was last reviewed, to compare with.
/// </summary>
public sealed record ConnectionToolSummary(
    Guid Id,
    Guid ConnectionId,
    string Name,
    string ModelName,
    string? Title,
    string Description,
    string Sensitivity,
    string State,
    DateTimeOffset LastSeenAt)
{
    #region Properties

    public string InputSchema { get; init; } = "{}";

    public bool NeedsReview { get; init; }

    public ToolDefinitionSnapshot? Reviewed { get; init; }

    #endregion
}

/// <summary>A tool's title, description, and input schema (JSON text) as they were when a person last reviewed them.</summary>
public sealed record ToolDefinitionSnapshot(string? Title, string Description, string InputSchema);

/// <summary>
/// Input only: a new tool connection. With <see cref="AuthKind"/> "header", the header name and value are required;
/// the value is a secret, goes to the vault, and is never part of a response.
/// </summary>
public sealed record CreateToolConnectionRequest(
    string? Name,
    string? Url,
    string? AuthKind = null,
    string? HeaderName = null,
    string? HeaderValue = null,
    string? Trust = null,
    string? ProtocolMode = null,
    string? ClientId = null,
    string? ClientSecret = null,
    string? SignInScope = null,
    string? Issuer = null)
{
    public override string ToString() => nameof(CreateToolConnectionRequest);
}

/// <summary>
/// What asking to sign in to a tool server came to. <see cref="Step"/> is redirect (open
/// <see cref="AuthorizationUrl"/> in the person's browser; the tool server sends them back to Drover when they are
/// done), connected (nothing to do), or failed (<see cref="Problem"/> says why). The address is the tool server's:
/// open it in a new browser tab or the system browser, never inside the app.
/// </summary>
public sealed record ToolSignInStart(string Step, string? AuthorizationUrl, string? Problem);

/// <summary>Input only: a new header for a header connection. Never part of a response.</summary>
public sealed record ReplaceToolHeaderRequest(string? HeaderName, string? HeaderValue)
{
    public override string ToString() => nameof(ReplaceToolHeaderRequest);
}

/// <summary>How far the connection's results are trusted, and which protocol generation it may use.</summary>
public sealed record UpdateToolConnectionRequest(string? Trust, string? ProtocolMode);

/// <summary>
/// What asking the server for its tools came to. <see cref="Problem"/> is set when the server could not answer; the
/// tools are then the ones already known. The counts say what the answer changed.
/// </summary>
public sealed record ToolConnectionRefresh(
    ToolConnectionSummary Connection,
    IReadOnlyList<ConnectionToolSummary> Tools,
    string? Problem,
    int Added,
    int Changed,
    int Missing,
    int GrantsForReview);
