namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Keeps vendor and resilience types inside this project: failures leave as <see cref="ModelProviderException"/>
/// and responses leave without the vendor's raw objects attached.
/// </summary>
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
            var response = await base.GetResponseAsync(messages, options, cancellationToken);

            StripVendorTypes(response);

            return response;
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

            StripVendorTypes(updates.Current);

            yield return updates.Current;
        }
    }

    #endregion

    #region Failure Mapping

    /// <summary>
    /// Finds the failure Drover understands anywhere in the exception chain. The vendor's own exception is not
    /// kept as the inner exception, so no vendor type leaves the adapter.
    /// </summary>
    internal static bool TryMap(Exception exception, out ModelProviderException mapped)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var found = Describe(current);

            if (found is not null)
            {
                mapped = found;

                return true;
            }
        }

        mapped = null!;

        return false;
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

    private static ModelProviderException? Describe(Exception exception) =>
        exception switch
        {
            ClientResultException failure => ModelProviderException.FromStatus(failure.Status, ReadRetryAfter(failure)),
            BrokenCircuitException => ModelProviderException.FromCircuitOpen(),
            TimeoutRejectedException => ModelProviderException.FromTimeout(),
            HttpRequestException or HttpIOException => ModelProviderException.FromNetwork(exception),
            _ => null,
        };

    private static TimeSpan? ReadRetryAfter(ClientResultException failure) =>
        failure.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true
            ? ParseRetryAfter(value, DateTimeOffset.UtcNow)
            : null;

    #endregion

    #region Vendor Types

    // The vendor client attaches its own objects to everything it returns. Nothing past this project may see them.
    private static void StripVendorTypes(ChatResponse response)
    {
        response.RawRepresentation = null;

        foreach (var message in response.Messages)
        {
            StripVendorTypes(message.Contents);
            message.RawRepresentation = null;
        }
    }

    private static void StripVendorTypes(ChatResponseUpdate update)
    {
        update.RawRepresentation = null;
        StripVendorTypes(update.Contents);
    }

    private static void StripVendorTypes(IList<AIContent> contents)
    {
        foreach (var content in contents)
        {
            content.RawRepresentation = null;

            foreach (var annotation in content.Annotations ?? [])
            {
                annotation.RawRepresentation = null;
            }
        }
    }

    #endregion
}
