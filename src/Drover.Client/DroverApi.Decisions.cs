namespace Drover.Client;

/// <summary>
/// The space's decision model, which checks bot rules written as a sentence. A key is sent when connecting or
/// replacing it and never comes back. The server allows reading to any member and every change to owners and admins.
/// </summary>
public sealed partial class DroverApi
{
    #region Decision Model

    public Task<DecisionModelState> GetDecisionModel(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<DecisionModelState>($"{Space(spaceId)}/decision-model", cancellationToken);

    public Task<DecisionModelResponse> SetDecisionModel(Guid spaceId, SetDecisionModelRequest request, CancellationToken cancellationToken = default) =>
        Send<DecisionModelResponse>(HttpMethod.Put, $"{Space(spaceId)}/decision-model", request, cancellationToken);

    public Task<DecisionModelResponse> ReplaceDecisionModelKey(Guid spaceId, ReplaceDecisionKeyRequest request, CancellationToken cancellationToken = default) =>
        Send<DecisionModelResponse>(HttpMethod.Put, $"{Space(spaceId)}/decision-model/key", request, cancellationToken);

    public Task<DecisionModelTestResponse> TestDecisionModel(Guid spaceId, CancellationToken cancellationToken = default) =>
        Post<DecisionModelTestResponse>($"{Space(spaceId)}/decision-model/test", null, cancellationToken);

    public Task DisconnectDecisionModel(Guid spaceId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/decision-model", cancellationToken);

    #endregion
}
