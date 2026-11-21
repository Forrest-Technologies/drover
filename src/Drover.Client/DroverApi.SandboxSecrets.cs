namespace Drover.Client;

/// <summary>
/// Sandbox secrets: credentials bots use in their sandboxes. A value is sent on create and rotate and never comes
/// back. The server allows these calls to a space's owners and admins, except a bot's names, which members may read.
/// </summary>
public sealed partial class DroverApi
{
    #region Sandbox Secrets

    public Task<List<SandboxSecretResponse>> ListSandboxSecrets(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<List<SandboxSecretResponse>>($"{Space(spaceId)}/sandbox-secrets", cancellationToken);

    public Task<SandboxSecretResponse> CreateSandboxSecret(Guid spaceId, CreateSandboxSecretRequest request, CancellationToken cancellationToken = default) =>
        Post<SandboxSecretResponse>($"{Space(spaceId)}/sandbox-secrets", request, cancellationToken);

    public Task<SandboxSecretResponse> RotateSandboxSecret(Guid spaceId, Guid secretId, RotateSandboxSecretRequest request, CancellationToken cancellationToken = default) =>
        Send<SandboxSecretResponse>(HttpMethod.Put, $"{Space(spaceId)}/sandbox-secrets/{secretId}/value", request, cancellationToken);

    public Task<SandboxSecretResponse> ChangeSandboxSecretScope(Guid spaceId, Guid secretId, ChangeSandboxSecretScopeRequest request, CancellationToken cancellationToken = default) =>
        Send<SandboxSecretResponse>(HttpMethod.Put, $"{Space(spaceId)}/sandbox-secrets/{secretId}/scope", request, cancellationToken);

    public Task<SandboxSecretResponse> ChangeSandboxSecretBinding(Guid spaceId, Guid secretId, ChangeSandboxSecretBindingRequest request, CancellationToken cancellationToken = default) =>
        Send<SandboxSecretResponse>(HttpMethod.Put, $"{Space(spaceId)}/sandbox-secrets/{secretId}/binding", request, cancellationToken);

    public Task<SandboxSecretResponse> ChangeSandboxSecretDescription(Guid spaceId, Guid secretId, ChangeSandboxSecretDescriptionRequest request, CancellationToken cancellationToken = default) =>
        Send<SandboxSecretResponse>(HttpMethod.Put, $"{Space(spaceId)}/sandbox-secrets/{secretId}/description", request, cancellationToken);

    public Task DeleteSandboxSecret(Guid spaceId, Guid secretId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/sandbox-secrets/{secretId}", cancellationToken);

    public Task<List<SandboxSecretHistoryEntry>> GetSandboxSecretHistory(Guid spaceId, Guid secretId, CancellationToken cancellationToken = default) =>
        Get<List<SandboxSecretHistoryEntry>>($"{Space(spaceId)}/sandbox-secrets/{secretId}/history", cancellationToken);

    public Task<List<BotSandboxSecretName>> ListBotSandboxSecretNames(Guid spaceId, Guid botId, CancellationToken cancellationToken = default) =>
        Get<List<BotSandboxSecretName>>($"{Space(spaceId)}/bots/{botId}/sandbox/secrets", cancellationToken);

    #endregion
}
