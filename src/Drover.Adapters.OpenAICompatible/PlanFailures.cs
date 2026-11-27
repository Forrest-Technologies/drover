namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The error codes a ChatGPT plan answers with, in a body before the stream opens or in <c>response.failed</c>, and
/// what each means to the person. Source, read 2026-11-27: https://developers.openai.com/siwc/llms-full.txt, "Errors and
/// recovery", "Structured Responses errors". The provider's own message is never repeated: it can quote the input.
/// </summary>
internal static class PlanFailures
{
    #region Private Fields

    public const string LimitReached = "Your ChatGPT plan has reached its limit for now.";

    public const string NotEligible = "This ChatGPT account cannot be used in other apps. You can add an API key instead.";

    public const string Unavailable = "ChatGPT could not check your plan just now. Try again shortly.";

    public const string UnsupportedRequest =
        "ChatGPT does not take part of this request on a personal plan. Leave out the file, tool or setting and try again.";

    public const string RouteNotSupported = "ChatGPT does not take this kind of request on a personal plan.";

    public const string SignInAgain = "ChatGPT did not accept your sign-in. Sign in again.";

    public const string NotAuthorized = "ChatGPT did not allow this request for your sign-in. Sign in again and allow plan use.";

    #endregion

    #region Public Methods

    /// <summary>
    /// The failure for a plan code, or null for a code that is not one of them. The usage limit is out of credit with no
    /// time to try again: the provider says not to infer a reset time. A person who is not eligible is refused, and
    /// trying again or signing in again changes nothing.
    /// </summary>
    public static ModelProviderException? For(string? code) =>
        code switch
        {
            "subscription_sharing_usage_limit_exceeded" => new(ModelFailure.OutOfCredit, LimitReached),

            // A plan signed in by code answers with the provider's command line tool's codes, in error.type on a 429 and
            // in error.code inside a stream (that tool's published error mapping, read 2026-11-27).
            "usage_limit_reached" or "usage_not_included" => new(ModelFailure.OutOfCredit, LimitReached),
            "subscription_sharing_user_not_eligible" => ModelProviderException.FromRefusal(NotEligible),
            "subscription_sharing_usage_unavailable" or "subscription_sharing_user_unavailable" =>
                new(ModelFailure.RetryLater, Unavailable),
            "subscription_sharing_unsupported_capability" => ModelProviderException.FromRefusal(UnsupportedRequest),
            "subscription_sharing_route_not_supported" => ModelProviderException.FromRefusal(RouteNotSupported),
            "subscription_sharing_invalid_user" => new(ModelFailure.InvalidKey, SignInAgain),
            "chatpass_v2_scope_not_authorized" or "chatpass_v2_invalid_authorization_context" =>
                ModelProviderException.FromRefusal(NotAuthorized),
            _ => null,
        };

    /// <summary>
    /// The failure for the plan code in an error body (<c>{"error":{"code":...}}</c>, or <c>{"error":{"type":...}}</c> as a
    /// plan signed in by code answers), or null when it carries none.
    /// </summary>
    public static ModelProviderException? Read(BinaryData? body)
    {
        if (body is null || body.ToMemory().Length is 0 or > 65536)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body.ToMemory());
            var root = document.RootElement;
            return root.ValueKind is JsonValueKind.Object && root.TryGetProperty("error", out var error) && error.ValueKind is JsonValueKind.Object
                ? For(Text(error, "code")) ?? For(Text(error, "type"))
                : null;
        }
        // A body that is not the provider's error object (admission can answer {"detail":"..."}) has no plan code.
#pragma warning disable DRV0005
        catch (JsonException)
        {
            return null;
        }
#pragma warning restore DRV0005
    }

    #endregion

    #region Private Methods

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    #endregion
}
