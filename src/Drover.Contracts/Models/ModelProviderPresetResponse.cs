namespace Drover.Contracts.Models;

/// <summary>
/// A named provider the server offers: where its chat protocol answers and whether it needs a key. It carries no
/// secret. <see cref="KeyRule"/> is one of <see cref="ModelProviderKeyRules"/>. When <see cref="AddressBlank"/> is set,
/// <see cref="Address"/> holds <see cref="AddressBlanks.Marker"/> once, where the one value only the person knows goes.
/// </summary>
public sealed record ModelProviderPresetResponse(
    string Id,
    string DisplayName,
    string Address,
    string KeyRule,
    string? KeyPage,
    bool Local,
    ModelProviderAddressBlankResponse? AddressBlank = null);

/// <summary>The one value a named provider's address needs from the person: what to call it and what it may hold.</summary>
/// <param name="Label">The field's label, for example "Account ID".</param>
/// <param name="Pattern">A regular expression the whole value must match. It never allows a character that could end a
/// host or a path segment: <c>/ @ : ? #</c> or white space.</param>
public sealed record ModelProviderAddressBlankResponse(string Label, string Pattern);

/// <summary>Builds the address of a named provider from the person's one value, refusing a value its pattern does not allow.</summary>
public static class AddressBlanks
{
    #region Public Methods

    /// <summary>The address with the value in place of the marker, or null when the value is empty or does not match.</summary>
    public static string? Build(string address, string pattern, string? value)
    {
        return !string.IsNullOrEmpty(value) && Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
            ? address.Replace(Marker, value, StringComparison.Ordinal)
            : null;
    }

    #endregion

    #region Constants

    public const string Marker = "{blank}";

    #endregion
}

/// <summary>The words the server sends for whether a named provider needs a key.</summary>
public static class ModelProviderKeyRules
{
    #region Rules

    public const string Required = "required";

    public const string Optional = "optional";

    public const string None = "none";

    #endregion
}
