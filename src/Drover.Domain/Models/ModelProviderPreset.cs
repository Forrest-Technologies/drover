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
/// <param name="AddressBlank">The one value the address needs from the person, or null. <paramref name="Address"/> then
/// holds <see cref="AddressBlanks.Marker"/> once.</param>
public sealed record ModelProviderPreset(
    string Id,
    string DisplayName,
    string Address,
    ModelProviderKeyRule KeyRule,
    string? KeyPage,
    bool Local,
    ModelProviderAddressBlank? AddressBlank = null)
{
    #region Public Methods

    /// <summary>The address for the person's value, or null when the value is not allowed. A row with no blank answers its own address.</summary>
    public string? AddressFor(string? value) =>
        AddressBlank is null ? Address : AddressBlanks.Build(Address, AddressBlank.Pattern, value);

    #endregion
}

/// <summary>What a named provider's address asks of the person, and what the value may hold.</summary>
/// <param name="Label">The field's label.</param>
/// <param name="Pattern">A regular expression the whole value must match, never allowing <c>/ @ : ? #</c> or white space.</param>
public sealed record ModelProviderAddressBlank(string Label, string Pattern);
