namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Where one Responses call goes and what it carries, decided by the provider class for each call. A key connection
/// sends the provider's fixed address and the key; a sign-in kind sends its own address, the person's token and the
/// extra headers its provider asks for. The credential travels as <c>Authorization: Bearer</c> on every request.
/// </summary>
/// <param name="Address">The base address the Responses path is added to, for example https://api.openai.com/v1/.</param>
/// <param name="Credential">The key or token for this call. Never stored beyond the call.</param>
/// <param name="Headers">Headers added to every request of the call, by name. Empty for a key connection.</param>
internal sealed record ResponsesDestination(Uri Address, string Credential, IReadOnlyDictionary<string, string> Headers);
