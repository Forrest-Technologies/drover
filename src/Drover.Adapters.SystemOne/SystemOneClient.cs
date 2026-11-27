namespace Drover.Adapters.SystemOne;

/// <summary>
/// A decision model on one connection. Questions beyond what one request of the kind takes go out as several
/// requests, one after another, and the caller sees one answer. Every request is answered once: there is no retry,
/// because the caller bounds the time a check may take. Failures surface as <see cref="ModelProviderException"/>
/// with words a person setting up the connection can act on, never with the key or the provider's own text.
/// </summary>
internal sealed class SystemOneClient(SystemOneKind kind, HttpClient http, Uri address, string? apiKey, string model, int maxAnswerBytes) : IDecisionModel
{
    #region Interface Implementations

    public async Task<DecisionAnswer> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dictionary<string, DecisionResult> answers = [];
        long inputTokens = 0;
        var numbered = request.Questions.Select((question, index) => (Index: index, Question: question)).ToList();

        foreach (var batch in numbered.Chunk(kind.MaxQuestionsPerRequest))
        {
            var (read, tokens) = await Send(request, batch, cancellationToken);
            inputTokens += tokens;

            foreach (var (index, question) in batch)
            {
                if (read.TryGetValue(SystemOneWire.WireName(index), out var result))
                {
                    answers[question.Name] = result;
                }
            }
        }

        return new DecisionAnswer(answers, inputTokens);
    }

    #endregion

    #region Private Methods

    private async Task<(Dictionary<string, DecisionResult> Answers, long InputTokens)> Send(
        DecisionRequest request,
        IReadOnlyList<(int Index, DecisionQuestion Question)> batch,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, kind.RequestAddress(address, model))
        {
            Content = new StringContent(SystemOneWire.Request(model, request, batch), Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrEmpty(apiKey))
        {
            message.Headers.TryAddWithoutValidation(kind.KeyHeader.Name, kind.KeyHeader.Prefix is { } prefix ? $"{prefix} {apiKey}" : apiKey);
        }

        using var response = await Exchange(message, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ModelProviderException.FromStatus((int)response.StatusCode, RetryAfter(response));
        }

        var body = await ReadBounded(response, cancellationToken);
        return SystemOneWire.Read(Parse(body), kind.Enveloped) ?? throw NotThisApi();
    }

    private async Task<HttpResponseMessage> Exchange(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException failure) when ((failure as OutboundBlockedException ?? failure.InnerException as OutboundBlockedException) is { } blocked)
        {
            throw ModelProviderException.FromRefusal(blocked.Message);
        }
        catch (HttpRequestException failure)
        {
            throw ModelProviderException.FromNetwork(failure);
        }
    }

    // At most the bound, read as it arrives: an answer larger than any real one is refused before it is all read.
    private async Task<byte[]> ReadBounded(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maxAnswerBytes)
        {
            throw TooLarge();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int count;

        while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > maxAnswerBytes)
            {
                throw TooLarge();
            }

            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }

        return buffer.ToArray();
    }

    private static JsonElement Parse(byte[] body)
    {
        try
        {
            return JsonElement.Parse(body);
        }
        catch (JsonException)
        {
            throw NotThisApi();
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response) =>
        response.Headers.RetryAfter is { } retry
            ? retry.Delta ?? (retry.Date is { } date ? date - DateTimeOffset.UtcNow : null)
            : null;

    private static ModelProviderException NotThisApi() =>
        new(ModelFailure.Rejected, "The server answered, but not in the decision model API Drover speaks. Check the address.");

    private static ModelProviderException TooLarge() =>
        new(ModelFailure.Rejected, "The decision model's answer is larger than any answer to these questions can be, so it was refused.");

    #endregion
}
