namespace Drover.Domain.Models;

/// <summary>Transport destination only. Persistent connection rows and credentials remain separate.</summary>
public sealed record ModelEndpoint(string Kind, Uri BaseAddress)
{
    /// <summary>What model discovery accepts from this endpoint.</summary>
    public DiscoveryLimits Limits { get; init; } = DiscoveryLimits.Default;

    /// <summary>
    /// On a connection each person signs in to, what the person's own sign-in says about where and how to call the
    /// provider (<see cref="ModelTokens.Details"/>, such as which of the kind's flows it was made by). It holds no token.
    /// Empty for a key connection.
    /// </summary>
    public IReadOnlyDictionary<string, string> SignInDetails { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Models of this connection whose facts are already stored. A provider that must ask for the facts of each model
    /// one by one asks for the others first, so a pass that runs out of time still moves on.
    /// </summary>
    public IReadOnlySet<string> Filled { get; init; } = new HashSet<string>();
}
