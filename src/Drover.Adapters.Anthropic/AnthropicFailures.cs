namespace Drover.Adapters.Anthropic;

/// <summary>
/// Turns the provider's failures, and the transport's, into Drover's. The provider's own message is never repeated,
/// because it can quote the input; it is read only to tell two causes of one status apart where the provider gives no
/// code. The vendor's exception is never kept as the inner exception, so no vendor type leaves the adapter.
/// Sources (read 2026-11-27): https://platform.claude.com/docs/en/api/errors (the statuses and their error types: 401
/// <c>authentication_error</c>, 402 <c>billing_error</c>, 403 <c>permission_error</c>, 429 <c>rate_limit_error</c>, 529
/// <c>overloaded_error</c>; the body <c>{"type":"error","error":{"type","message","details":{"error_code"}}}</c>; a
/// stream's error event with the same <c>error</c> object), https://platform.claude.com/docs/en/api/$shared (the
/// <c>error_code</c> values, among them <c>enforced_spend_limit_reached</c> and <c>contract_spend_limit_reached</c>) and
/// https://platform.claude.com/docs/en/api/rate-limits (<c>retry-after</c>, not sent on every 429).
/// </summary>
internal static class AnthropicFailures
{
    #region Private Fields

    /// <summary>The most of an error body that is read. An error body is a few hundred bytes.</summary>
    private const int MaxErrorBytes = 64 * 1024;

    /// <summary>
    /// Unconfirmed on the provider's pages: the words of the 400 an organization with no prepaid credit gets, as public
    /// reports of the error quote them ("Your credit balance is too low to access the Anthropic API"). The errors page
    /// names no code for it.
    /// </summary>
    private const string CreditTooLow = "credit balance is too low";

    /// <summary>
    /// Unconfirmed on the provider's pages: the start of the 400 a conversation longer than the model's window gets
    /// ("prompt is too long: N tokens > M maximum"). The errors page names no code for it.
    /// </summary>
    private const string PromptTooLong = "prompt is too long";

    private static readonly string[] spendLimitCodes =
        ["enforced_spend_limit_reached", "contract_spend_limit_reached", "linked_organization_spend_limit_reached"];

    #endregion

    #region Public Methods

    /// <summary>Finds the failure Drover understands anywhere in the exception chain.</summary>
    public static bool TryMap(Exception exception, out ModelProviderException mapped, TimeSpan? retryAfter = null)
    {
        mapped = null!;

        if (exception is OperationCanceledException and not TaskCanceledException { InnerException: TimeoutException })
        {
            return false;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OutboundBlockedException blocked)
            {
                mapped = ModelProviderException.FromRefusal(blocked.Message);
                return true;
            }
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (Describe(current, retryAfter) is { } found)
            {
                mapped = found;
                return true;
            }
        }

        return false;
    }

    /// <summary>A failed answer to a request the adapter sent itself, such as the model list.</summary>
    public static async Task<ModelProviderException> FromResponse(HttpResponseMessage response, int maxBytes, CancellationToken cancellation)
    {
        var retryAfter = ParseRetryAfter(
            response.Headers.TryGetValues("retry-after", out var values) ? values.FirstOrDefault() : null,
            DateTimeOffset.UtcNow);
        var body = await ReadErrorBody(response.Content, Math.Min(maxBytes, MaxErrorBytes), cancellation);
        return FromError((int)response.StatusCode, body, retryAfter);
    }

    /// <summary>A status and the provider's error object, as Drover's failure.</summary>
    public static ModelProviderException FromError(int? status, ProviderError error, TimeSpan? retryAfter)
    {
        if (error.Code is { } code && spendLimitCodes.Contains(code))
        {
            return new(ModelFailure.OutOfCredit, "The provider says the account has reached its spend limit.");
        }

        if (error.Type is "billing_error" || error.Message?.Contains(CreditTooLow, StringComparison.OrdinalIgnoreCase) == true)
        {
            return ModelProviderException.FromStatus((int)HttpStatusCode.PaymentRequired);
        }

        if (error.Message?.Contains(PromptTooLong, StringComparison.OrdinalIgnoreCase) == true)
        {
            return new(ModelFailure.TooLong, "The provider says this conversation is longer than the model can read.");
        }

        return (error.Type, status) switch
        {
            ("overloaded_error", _) or (_, 529) => new(
                ModelFailure.RetryLater,
                "The provider is overloaded right now. Try again in a moment.",
                retryAfter),
            ("rate_limit_error", _) => ModelProviderException.FromStatus((int)HttpStatusCode.TooManyRequests, retryAfter),
            ("authentication_error" or "permission_error", _) => ModelProviderException.FromStatus((int)HttpStatusCode.Unauthorized),
            (_, { } known) => ModelProviderException.FromStatus(known, retryAfter),
            ("api_error" or "timeout_error", null) => ModelProviderException.FromStatus((int)HttpStatusCode.InternalServerError),
            _ => new(ModelFailure.Rejected, "The provider stopped before the reply was finished."),
        };
    }

    public static TimeSpan? ParseRetryAfter(string? value, DateTimeOffset now)
    {
        if (double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date > now ? date - now : TimeSpan.Zero;
        }

        return null;
    }

    #endregion

    #region Private Methods

    private static ModelProviderException? Describe(Exception exception, TimeSpan? retryAfter) => exception switch
    {
        AnthropicApiException failure => FromError((int)failure.StatusCode, ProviderError.Parse(failure.ResponseBody), retryAfter),
        AnthropicSseException failure => FromError(null, ProviderError.From(failure.Body), retryAfter),
        AnthropicInvalidDataException => ModelProviderException.FromMalformedResponse(),
        BrokenCircuitException => ModelProviderException.FromCircuitOpen(),
        TimeoutRejectedException or TaskCanceledException { InnerException: TimeoutException } => ModelProviderException.FromTimeout(),
        AnthropicIOException or HttpRequestException or HttpIOException or IOException => ModelProviderException.FromNetwork(),
        JsonException => ModelProviderException.FromMalformedResponse(),
        _ => null,
    };

    private static async Task<ProviderError> ReadErrorBody(HttpContent content, int maxBytes, CancellationToken cancellation)
    {
        try
        {
            using var document = await BoundedResponse.ReadJson(content, maxBytes, "error", cancellation);
            return ProviderError.From(document.RootElement);
        }
        // An error body that is too large or not JSON says nothing more than its status does.
#pragma warning disable DRV0005
        catch (Exception exception) when (exception is JsonException or ModelProviderException)
        {
            return ProviderError.None;
        }
#pragma warning restore DRV0005
    }

    #endregion
}

/// <summary>What the provider said went wrong: the error's type, its message (never shown), and its code.</summary>
internal sealed record ProviderError(string? Type, string? Message, string? Code)
{
    #region Properties

    public static ProviderError None { get; } = new(null, null, null);

    #endregion

    #region Public Methods

    public static ProviderError Parse(string body)
    {
        if (string.IsNullOrEmpty(body) || body.Length > 64 * 1024)
        {
            return None;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return From(document.RootElement);
        }
        // A body that is not JSON says nothing more than its status does.
#pragma warning disable DRV0005
        catch (JsonException)
        {
            return None;
        }
#pragma warning restore DRV0005
    }

    /// <summary>Reads <c>{"error": {"type", "message", "details": {"error_code"}}}</c>, or the error object itself.</summary>
    public static ProviderError From(JsonElement root)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return None;
        }

        var error = root.TryGetProperty("error", out var inner) && inner.ValueKind is JsonValueKind.Object ? inner : root;
        var details = error.TryGetProperty("details", out var found) && found.ValueKind is JsonValueKind.Object ? found : default;
        return new(Text(error, "type"), Text(error, "message"), Text(details, "error_code"));
    }

    #endregion

    #region Private Methods

    private static string? Text(JsonElement parent, string name) =>
        parent.ValueKind is JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    #endregion
}
