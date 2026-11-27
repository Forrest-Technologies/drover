namespace Drover.Engine.Models;

/// <summary>
/// The chat client of a run on a connection each person signs in to. Before every model call it asks
/// <see cref="ModelCredentials.Ready"/> for the person's credential, which renews the access token when it runs out
/// soon, and builds the provider's client with it for that one call. So a run that lasts longer than its token goes
/// on with a new one. A credential that cannot be had ends the call with <see cref="ModelCredentialException"/>.
/// Each token it is handed is passed to <paramref name="revealed"/> first, so the run can scrub it from what it stores.
/// No tenant transaction is open here: the caller has none, and the renewal opens and closes its own.
/// </summary>
public sealed class RenewingChatClient(
    IModelClientProvider provider,
    ModelEndpoint endpoint,
    string model,
    ModelSettings settings,
    ModelCredentials credentials,
    ModelCredential credential,
    Action<string?> revealed) : IModelChatClient
{
    #region Private Fields

    private readonly SemaphoreSlim gate = new(1, 1);

    private ModelCredential current = credential;

    #endregion

    #region Interface Implementations

    public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
    {
        var ready = await Ready(cancellation);
        using var client = provider.CreateChatClient(ready.For(endpoint), ready.Value, model, settings);
        return await client.GetResponse(request, cancellation);
    }

    public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(
        ModelChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var ready = await Ready(cancellation);
        using var client = provider.CreateChatClient(ready.For(endpoint), ready.Value, model, settings);

        await foreach (var update in client.GetStreamingResponse(request, cancellation))
        {
            yield return update;
        }
    }

    public void Dispose() => gate.Dispose();

    #endregion

    #region Private Methods

    /// <summary>The credential for this call, renewed first when it runs out soon. One caller at a time, so parallel calls share one renewal.</summary>
    private async Task<ModelCredential> Ready(CancellationToken cancellation)
    {
        await gate.WaitAsync(cancellation);

        try
        {
            current = await credentials.Ready(current, cancellation);
        }
        finally
        {
            gate.Release();
        }

        if (current.Problem is not null)
        {
            throw new ModelCredentialException(current);
        }

        revealed(current.Value);
        return current;
    }

    #endregion
}
