namespace Drover.Domain.Models;

/// <summary>
/// What a provider kind is called and what a connection of that kind asks for, so a client can draw the form
/// without knowing the kind. It is shown to every signed-in person of the server, so it carries words and rules
/// only: never a key, a header, or a balance. The one addresses it may carry are those of the named providers of a
/// kind that takes an address (<paramref name="NamedProviders"/>), which are public and reviewed.
/// </summary>
/// <param name="Kind">The connection kind, for example "venice". Unique among the providers of a server.</param>
/// <param name="DisplayName">What a person calls the kind.</param>
/// <param name="KeyRule">Whether a connection needs a key.</param>
/// <param name="AddressRule">Whether a connection supplies its own address.</param>
/// <param name="AddressHint">One sentence under the address field, or null when the kind has no address field.</param>
/// <param name="KeyHint">One sentence under the key field, or null when the kind has no key field.</param>
/// <param name="ReportsCapabilities">True when the provider's model list carries capabilities and prices.</param>
/// <param name="NamedProviders">The providers a person can pick by name for a kind that takes an address, in the order to
/// show them. Empty for every other kind.</param>
/// <param name="SignIn">True when each person connects this kind by signing in, not by pasting a key.</param>
public sealed record ModelProviderDescriptor(
    string Kind,
    string DisplayName,
    ModelKeyRule KeyRule,
    ModelAddressRule AddressRule,
    string? AddressHint,
    string? KeyHint,
    bool ReportsCapabilities,
    IReadOnlyList<ModelProviderPreset>? NamedProviders = null,
    bool SignIn = false);
