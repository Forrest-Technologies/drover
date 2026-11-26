namespace Drover.Adapters.OpenAICompatible;

/// <summary>Sanitizes SDK, transport and resilience failures at the provider boundary.</summary>
internal static class ProtocolFailureMapping
{
    #region Failure Mapping

    /// <summary>
    /// Finds the failure Drover understands anywhere in the exception chain. The vendor's own exception is not
    /// kept as the inner exception, so no vendor type leaves the adapter.
    /// </summary>
    internal static bool TryMap(Exception exception, out ModelProviderException mapped)
    {
        if (exception is OperationCanceledException)
        {
            mapped = null!;
            return false;
        }

        if (FindRefusal(exception) is { } refusal)
        {
            mapped = refusal;

            return true;
        }

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

    /// <summary>A refusal by policy wins over whatever wrapped it, because the wrapper only says "no response".</summary>
    internal static ModelProviderException? FindRefusal(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OutboundBlockedException blocked)
            {
                return ModelProviderException.FromRefusal(blocked.Message);
            }
        }

        return null;
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
            ClientResultException { Status: 400 } failure when ContextLengthExceeded(failure) =>
                new ModelProviderException(ModelFailure.TooLong, "The provider says this conversation is longer than the model can read."),
            ClientResultException failure => ModelProviderException.FromStatus(failure.Status, ReadRetryAfter(failure)),
            BrokenCircuitException => ModelProviderException.FromCircuitOpen(),
            TimeoutRejectedException => ModelProviderException.FromTimeout(),
            HttpRequestException or HttpIOException or JsonException => ModelProviderException.FromNetwork(),
            _ => null,
        };

    private static bool ContextLengthExceeded(ClientResultException failure)
    {
        if (failure.GetRawResponse()?.Content is not { } content || content.ToMemory().Length > 65536)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(content.ToMemory());
            return document.RootElement.ValueKind is JsonValueKind.Object && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind is JsonValueKind.Object && error.TryGetProperty("code", out var code)
                && code.ValueKind is JsonValueKind.String && code.ValueEquals("context_length_exceeded");
        }
        // An unrecognized error body keeps the ordinary HTTP 400 mapping; its contents can contain private input.
#pragma warning disable DRV0005
        catch (JsonException)
        {
            return false;
        }
    }

#pragma warning restore DRV0005

    private static TimeSpan? ReadRetryAfter(ClientResultException failure) =>
        failure.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true
            ? ParseRetryAfter(value, DateTimeOffset.UtcNow)
            : null;

    #endregion

}
