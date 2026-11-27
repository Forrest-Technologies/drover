namespace Drover.Engine.Runs;

/// <summary>
/// The model client a run's agent talks to. Every call through it is one step: it is counted against the step limit,
/// announced as <c>step.started</c>, given the message id the step's output is stored under, and refused outright if
/// a tenant transaction is open. Balances a provider reports with a reply are recorded on the connection.
/// </summary>
public sealed class MeteredModelClient(IModelChatClient inner, RunSession session, Guid connectionId, ILogger logger, ModelCapabilities? capabilities, string model, int? outputLimit, int keepRecent) : IModelChatClient
{
    #region Private Fields

    private double calibration = 1;

    #endregion

    #region Interface Implementations

    public async Task<ModelChatResponse> GetResponse(ModelChatRequest request, CancellationToken cancellation = default)
    {
        var refusedEstimate = (long?)null;
        while (true)
        {
            var (given, estimate, limit) = Prepare(request, refusedEstimate);
            var messageId = await BeginStep(estimate, given != request);
            try
            {
                var response = await inner.GetResponse(given, cancellation);
                Calibrate(response.Usage ?? response.Messages.SelectMany(message => message.Contents).OfType<ModelTokenUsage>().LastOrDefault(), given);
                await session.EndStep();
                await RecordBalances(response.Balances);
                return response with { Messages = [.. response.Messages.Select(message => message with { Id = messageId })] };
            }
            catch (ModelProviderException failure) when (failure.Failure is ModelFailure.TooLong)
            {
                await session.EndStep();
                if (refusedEstimate.HasValue)
                {
                    throw RequestCompaction.TooLong(model, estimate, limit);
                }

                refusedEstimate = estimate;
            }
        }
    }

    public async IAsyncEnumerable<ModelChatUpdate> GetStreamingResponse(
        ModelChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        var refusedEstimate = (long?)null;
        while (true)
        {
            var (given, estimate, limit) = Prepare(request, refusedEstimate);
            var messageId = await BeginStep(estimate, given != request);
            await using var stream = inner.GetStreamingResponse(given, cancellation).GetAsyncEnumerator(cancellation);
            IReadOnlyList<ModelBalance> balances = [];
            var emitted = false;
            while (true)
            {
                bool more;
                try
                {
                    more = await stream.MoveNextAsync();
                }
                catch (ModelProviderException failure) when (failure.Failure is ModelFailure.TooLong)
                {
                    await session.EndStep();
                    if (refusedEstimate.HasValue || emitted)
                    {
                        throw RequestCompaction.TooLong(model, estimate, limit);
                    }

                    refusedEstimate = estimate;
                    break;
                }

                if (!more)
                {
                    await session.EndStep();
                    await RecordBalances(balances);
                    yield break;
                }

                emitted = true;
                var update = stream.Current;
                if (update.Balances.Count > 0)
                {
                    balances = update.Balances;
                }

                Calibrate(update.Contents.OfType<ModelTokenUsage>().LastOrDefault(), given);
                yield return update with { MessageId = messageId };
            }
        }
    }

    public void Dispose() => inner.Dispose();

    #endregion

    #region Private Methods

    private (ModelChatRequest Request, long Estimate, long Limit) Prepare(ModelChatRequest request, long? refusedEstimate)
    {
        var estimate = (long)Math.Ceiling(calibration * TokenEstimate.Request(request));
        var limit = capabilities?.ContextTokens ?? 0;
        if (refusedEstimate is null && limit <= 0)
        {
            return (request, estimate, 0);
        }

        var budget = refusedEstimate is { } refused ? Math.Max(1, (long)(refused * 0.8)) : limit;
        if (limit > 0)
        {
            budget = Math.Min(limit, budget);
        }

        var reserve = request.Options.MaxOutputTokens ?? outputLimit ?? capabilities?.MaxOutputTokens ?? 4096;
        var given = RequestCompaction.Fit(request, model, budget, reserve, keepRecent, calibration);
        return (given, (long)Math.Ceiling(calibration * TokenEstimate.Request(given)), budget);
    }

    private void Calibrate(ModelTokenUsage? usage, ModelChatRequest request)
    {
        if (usage?.InputTokens is > 0)
        {
            calibration = TokenEstimate.Calibration(usage.InputTokens.Value, TokenEstimate.Request(request));
        }
    }

    private async Task<string> BeginStep(long estimate, bool shortened)
    {
        if (TenantScope.IsOpenInCurrentFlow)
        {
            throw new InvalidOperationException("A model was called inside a tenant transaction. Close the scope before the call.");
        }

        return (await session.BeginStep(estimate, shortened)).ToString("N");
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
