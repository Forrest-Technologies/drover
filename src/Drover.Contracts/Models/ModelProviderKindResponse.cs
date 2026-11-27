namespace Drover.Contracts.Models;

/// <summary>
/// One kind of model provider the server offers, and what a connection of it asks for. It carries words and rules
/// only: never a key, a header, or a balance. The one addresses it carries are those of <see cref="NamedProviders"/>,
/// the providers a person can pick by name for a kind that takes an address. <see cref="KeyRule"/> is one of
/// <see cref="ModelKeyRules"/>, <see cref="AddressRule"/> one of <see cref="ModelAddressRules"/>. <see cref="SignIn"/>
/// is true for a kind each person connects by signing in with their own account, which takes no key in the form.
/// </summary>
public sealed record ModelProviderKindResponse(
    string Kind,
    string DisplayName,
    string KeyRule,
    string AddressRule,
    string? AddressHint,
    string? KeyHint,
    bool ReportsCapabilities,
    IReadOnlyList<ModelProviderPresetResponse> NamedProviders,
    bool SignIn = false);

/// <summary>The words the server sends for whether a connection of a kind needs a key.</summary>
public static class ModelKeyRules
{
    #region Rules

    public const string Required = "required";

    public const string Optional = "optional";

    public const string None = "none";

    #endregion
}

/// <summary>The words the server sends for whether a connection of a kind supplies its own address.</summary>
public static class ModelAddressRules
{
    #region Rules

    /// <summary>Every connection of the kind uses the provider's one address and carries none.</summary>
    public const string Fixed = "fixed";

    /// <summary>The connection supplies its address.</summary>
    public const string Required = "required";

    #endregion
}
