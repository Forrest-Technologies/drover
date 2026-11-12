namespace Drover.Engine.Runs;

/// <summary>
/// The model client a run's agent talks to. Every call through it is one step: it is counted against the step limit,
/// announced as <c>step.started</c>, given the message id the step's output is stored under, and refused outright if
/// a tenant transaction is open. Balances a provider reports with a reply are recorded on the connection.
/// </summary>
public sealed class MeteredModelClient(IModelChatClient inner, RunSession session, Guid connectionId, ILogger logger) : IModelChatClient
{
    #region Interface Implementations

    public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
    {
        var messageId = await BeginStep();
        var response = await inner.GetResponse(request, cancellation);
        await session.EndStep();
        await RecordBalances(response.Balances);
        return response with { Messages = [.. response.Messages.Select(message => message with { Id = messageId })] };
    }

    public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(
        ModelChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var messageId = await BeginStep();
        IReadOnlyList<ModelBalance> balances = [];

        await foreach (var update in inner.GetStreamingResponse(request, cancellation))
        {
            if (update.Balances.Count > 0)
            {
                balances = update.Balances;
            }

            yield return update with { MessageId = messageId };
        }

        await session.EndStep();
        await RecordBalances(balances);
    }

    public void Dispose() => inner.Dispose();

    #endregion

    #region Private Methods

    private async Task<string> BeginStep()
    {
        if (TenantScope.IsOpenInCurrentFlow)
        {
            throw new InvalidOperationException("A model was called inside a tenant transaction. Close the scope before the call.");
        }

        return (await session.BeginStep()).ToString("N");
    }

    private async Task RecordBalances(IReadOnlyList<ModelBalance> balances)
    {
        if (balances.Count == 0)
        {
            return;
        }

        try
        {
            await session.Tenant.InScope(services => services.GetRequiredService<ModelConnectionService>()
                .RecordBalances(connectionId, balances, CancellationToken.None));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A balance is a convenience on the connection screen. Losing one must not fail the reply it came with.
            logger.LogWarning(exception, "Could not record the balance of model connection {ConnectionId}", connectionId);
        }
    }

    #endregion
}
