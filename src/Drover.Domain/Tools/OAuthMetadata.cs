namespace Drover.Domain.Tools;

/// <summary>
/// How Drover names itself to a tool server that wants a sign-in. No secret: tokens and client secrets are in the
/// vault, and <see cref="ClientSecretId"/> is only where to find one.
/// </summary>
public sealed record OAuthMetadata
{
    #region Properties

    public string? AuthorizationEndpoint { get; init; }

    public string? TokenEndpoint { get; init; }

    public string? ClientId { get; init; }

    public Guid? ClientSecretId { get; init; }

    /// <summary>
    /// The sign-in service the client id was given out by. A connection with a client id signs in with this
    /// service only, whatever the tool server later says.
    /// </summary>
    public string? Issuer { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = [];

    #endregion
}
