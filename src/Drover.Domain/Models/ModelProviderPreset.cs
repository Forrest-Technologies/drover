namespace Drover.Domain.Models;

/// <summary>Whether a named provider needs the person's key.</summary>
public enum ModelProviderKeyRule
{
    Required,
    Optional,
    None,
}

/// <summary>
/// A provider a person can pick by name instead of typing an address. It is a starting point, not a kind: a
/// connection made from it is stored as <see cref="ModelConnectionKinds.Compatible"/> with this address.
/// </summary>
/// <param name="Id">Lowercase and stable.</param>
/// <param name="DisplayName">The name a person reads.</param>
/// <param name="Address">Where the provider's chat protocol answers.</param>
/// <param name="KeyRule">Whether the provider needs a key.</param>
/// <param name="KeyPage">The provider's own page where a key is made. https only, or null.</param>
/// <param name="Local">True when the address is on the person's own machine or network.</param>
public sealed record ModelProviderPreset(
    string Id,
    string DisplayName,
    string Address,
    ModelProviderKeyRule KeyRule,
    string? KeyPage,
    bool Local);
