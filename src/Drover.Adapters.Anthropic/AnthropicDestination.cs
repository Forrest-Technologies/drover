namespace Drover.Adapters.Anthropic;

/// <summary>
/// Where one Messages call goes and what it carries, decided by the provider class for each client it builds. A key
/// connection sends the provider's fixed address and the key with no extra headers and no leading system block. A
/// sign-in kind built on this client later gives the person's token in place of the key, the headers its provider asks
/// for, and the system block that must come before the bot's instructions, with no other change to the client. The
/// credential always travels as <c>Authorization: Bearer</c>, never as <c>x-api-key</c>.
/// </summary>
/// <param name="Address">The base address the <c>v1/...</c> paths are added to: https://api.anthropic.com/ for a key.</param>
/// <param name="Credential">The key or token for this client. Never stored beyond the client.</param>
/// <param name="Headers">Headers added to every request of the client, by name. Empty for a key connection.</param>
/// <param name="SystemPrefix">A system block sent before the bot's instructions, or null for a key connection.</param>
internal sealed record AnthropicDestination(
    Uri Address,
    string Credential,
    IReadOnlyDictionary<string, string> Headers,
    string? SystemPrefix = null);
