namespace Drover.Adapters.OpenAICompatible;

/// <summary>Lets the vendor client's failures leave this project as <see cref="ModelProviderException"/>.</summary>
internal sealed class ErrorMappingChatClient(IChatClient inner) : DelegatingChatClient(inner)
{
    #region Interface Implementations

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception exception) when (TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var updates = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        while (true)
        {
            bool hasNext;

            try
            {
                hasNext = await updates.MoveNextAsync();
            }
            catch (Exception exception) when (TryMap(exception, out var mapped))
            {
                throw mapped;
            }

            if (!hasNext)
            {
                yield break;
            }

            yield return updates.Current;
        }
    }

    #endregion

    #region Failure Mapping

    internal static bool TryMap(Exception exception, out ModelProviderException mapped)
    {
        mapped = exception switch
        {
            ClientResultException failure => ModelProviderException.FromStatus(
                failure.Status,
                ReadRetryAfter(failure),
                failure),
            HttpRequestException network => ModelProviderException.FromNetwork(network),
            _ => null!,
        };

        return mapped is not null;
    }

    internal static TimeSpan? ParseRetryAfter(string? value, DateTimeOffset now)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date > now ? date - now : TimeSpan.Zero;
        }

        return null;
    }

    private static TimeSpan? ReadRetryAfter(ClientResultException failure) =>
        failure.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true
            ? ParseRetryAfter(value, DateTimeOffset.UtcNow)
            : null;

    #endregion
}
