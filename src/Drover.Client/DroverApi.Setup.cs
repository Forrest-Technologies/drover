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

    /// <summary>The kinds of model provider the server offers, in the order it lists them, and what each asks for.</summary>
    public async Task<IReadOnlyList<ModelProviderKindResponse>> ListModelProviderKinds(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync("api/model-provider-kinds", cancellationToken);
        return await response.ReadValue<List<ModelProviderKindResponse>>(cancellationToken);
    }

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

    /// <summary>
    /// Says what one model of a connection can do and costs, over what the provider reported. A member left null is
    /// cleared. A model the catalog does not have is added. Admins only.
    /// </summary>
    public Task<ModelCatalogResponse> SetModelSettings(
        Guid spaceId,
        Guid connectionId,
        SetModelSettingsRequest request,
        CancellationToken cancellationToken = default) =>
        Send<ModelCatalogResponse>(HttpMethod.Put, $"{Space(spaceId)}/model-connections/{connectionId}/model-settings", request, cancellationToken);

    /// <summary>Clears everything said about one model, so the provider's values are in effect again. Admins only.</summary>
    public Task ClearModelSettings(Guid spaceId, Guid connectionId, string model, CancellationToken cancellationToken = default) =>
        SendForNothing(
            HttpMethod.Delete,
            $"{Space(spaceId)}/model-connections/{connectionId}/model-settings?model={Uri.EscapeDataString(model)}",
            cancellationToken);

    /// <summary>
    /// Starts the caller's own sign-in to a sign-in kind. The request names where this device receives
    /// the browser's return. The answer is the page to open; no token ever comes to the device.
    /// </summary>
    public async Task<ModelSignInStarted> StartModelSignIn(
        Guid spaceId,
        StartModelSignInRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync($"api/spaces/{spaceId}/model-connections/sign-in", request, cancellationToken);
        return await response.ReadValue<ModelSignInStarted>(cancellationToken);
    }

    /// <summary>Hands the server the query string the browser brought back, and gets the connection once it is done.</summary>
    public async Task<ModelConnectionSummary> FinishModelSignIn(
        Guid spaceId,
        Guid attemptId,
        FinishModelSignInRequest request,
        CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsJsonAsync(
            $"api/spaces/{spaceId}/model-connections/sign-in/{attemptId}/finish",
            request,
            cancellationToken);
        return await response.ReadValue<ModelConnectionSummary>(cancellationToken);
    }

    /// <summary>Where the caller's own sign-in stands, read while the server finishes a sign-in by code.</summary>
    public async Task<ModelSignInProgress> ReadModelSignIn(Guid spaceId, Guid attemptId, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync($"api/spaces/{spaceId}/model-connections/sign-in/{attemptId}", cancellationToken);
        return await response.ReadValue<ModelSignInProgress>(cancellationToken);
    }

    /// <summary>Stops the caller's sign-in by code.</summary>
    public Task CancelModelSignIn(Guid spaceId, Guid attemptId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"api/spaces/{spaceId}/model-connections/sign-in/{attemptId}", cancellationToken);

    /// <summary>Signs the caller out of their own sign-in to a connection.</summary>
    public async Task SignOutOfModelConnection(Guid spaceId, Guid connectionId, CancellationToken cancellationToken = default)
    {
        using var response = await http.DeleteAsync($"api/spaces/{spaceId}/model-connections/{connectionId}/sign-in", cancellationToken);
        await response.ThrowIfRefused(cancellationToken);
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
