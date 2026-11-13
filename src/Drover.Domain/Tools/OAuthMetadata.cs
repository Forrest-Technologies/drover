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

    public IReadOnlyList<string> Scopes { get; init; } = [];

    #endregion
}
