namespace Drover.Domain.Tools;

/// <summary>What Drover learned about a tool server's OAuth setup. No secret: tokens and client secrets are in the vault.</summary>
public sealed record OAuthMetadata
{
    public string? AuthorizationEndpoint { get; init; }

    public string? TokenEndpoint { get; init; }

    public string? ClientId { get; init; }

    public IReadOnlyList<string> Scopes { get; init; } = [];
}
