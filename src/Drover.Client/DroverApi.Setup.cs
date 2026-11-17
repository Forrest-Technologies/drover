namespace Drover.Client;

/// <summary>
/// Account, first-run setup, spaces, and model connections.
/// </summary>
public sealed partial class DroverApi
{
    #region Account And Setup

    /// <summary>What first-run setup needs, or nothing when this server already has its first account.</summary>
    public async Task<SetupStatus?> GetSetupStatus(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/setup", cancellationToken);
        return await response.ReadValueUnless<SetupStatus>(HttpStatusCode.NotFound, cancellationToken);
    }

    public async Task<SessionResponse> CreateFirstAccount(SetupRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/setup", request, cancellationToken);
        return await response.ReadValue<SessionResponse>(cancellationToken);
    }

    public async Task<SessionResponse> SignIn(SignInRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/sign-in", request, cancellationToken);
        return await response.ReadValue<SessionResponse>(cancellationToken);
    }

    public async Task SignOut(CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync("api/sign-out", null, cancellationToken);
        await response.ThrowIfRefused(cancellationToken);
    }

    /// <summary>
    /// Changes the signed-in person's password, given the current one. Every other session of theirs ends; this one
    /// stays signed in.
    /// </summary>
    public async Task ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/account/password", request, cancellationToken);
        await response.ThrowIfRefused(cancellationToken);
    }

    /// <summary>
    /// Sets a new password with the one-time code the operator printed to the server log. Every session of that
    /// person ends, and they sign in again with the new password.
    /// </summary>
    public async Task ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync("api/password-reset", request, cancellationToken);
        await response.ThrowIfRefused(cancellationToken);
    }

    /// <summary>The signed-in person and their spaces, or nothing when nobody is signed in.</summary>
    public async Task<SessionResponse?> GetSession(CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/session", cancellationToken);
        return await response.ReadValueUnless<SessionResponse>(HttpStatusCode.Unauthorized, cancellationToken);
    }

    #endregion

    #region Spaces

    public async Task<SpaceResponse> GetSpace(Guid spaceId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/spaces/{spaceId}", cancellationToken);
        return await response.ReadValue<SpaceResponse>(cancellationToken);
    }

    #endregion

    #region Model Connections

    public async Task<IReadOnlyList<ModelConnectionSummary>> ListModelConnections(
        Guid spaceId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/spaces/{spaceId}/model-connections", cancellationToken);
        return await response.ReadValue<List<ModelConnectionSummary>>(cancellationToken);
    }

    public async Task<ModelConnectionSummary> CreateModelConnection(
        Guid spaceId,
        CreateModelConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync($"api/spaces/{spaceId}/model-connections", request, cancellationToken);
        return await response.ReadValue<ModelConnectionSummary>(cancellationToken);
    }

    public async Task<ModelConnectionSummary> ReplaceModelKey(
        Guid spaceId,
        Guid connectionId,
        UpdateModelKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PutAsJsonAsync(
            $"api/spaces/{spaceId}/model-connections/{connectionId}/key",
            request,
            cancellationToken);
        return await response.ReadValue<ModelConnectionSummary>(cancellationToken);
    }

    /// <summary>Asks the provider whether the connection works. The answer carries a sentence either way.</summary>
    public async Task<ConnectionTestResponse> TestModelConnection(
        Guid spaceId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(
            $"api/spaces/{spaceId}/model-connections/{connectionId}/test",
            null,
            cancellationToken);
        return await response.ReadValue<ConnectionTestResponse>(cancellationToken);
    }

    public async Task<IReadOnlyList<ModelCatalogResponse>> ListModels(
        Guid spaceId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(
            $"api/spaces/{spaceId}/model-connections/{connectionId}/models",
            cancellationToken);
        return await response.ReadValue<List<ModelCatalogResponse>>(cancellationToken);
    }

    public async Task<ModelRefreshResponse> RefreshModels(
        Guid spaceId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync(
            $"api/spaces/{spaceId}/model-connections/{connectionId}/models/refresh",
            null,
            cancellationToken);
        return await response.ReadValue<ModelRefreshResponse>(cancellationToken);
    }

    #endregion
}
