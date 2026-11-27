namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Which ChatGPT account a plan token signed in by code belongs to: the <c>chatgpt_account_id</c> member of the token's
/// <c>https://api.openai.com/auth</c> claim, which each call names in the <c>chatgpt-account-id</c> header (read from the
/// provider's command line tool's published source on 2026-11-27). The token is Drover's own, from the vault, so its
/// claims are read without checking its signature: this only says where a call goes, never who may make it.
/// </summary>
internal static class PlanAccount
{
    #region Private Fields

    public const string AuthClaim = "https://api.openai.com/auth";

    public const string AccountClaim = "chatgpt_account_id";

    #endregion

    #region Public Methods

    /// <summary>The account id a token names, or null when it is not a token that names one.</summary>
    public static string? Of(string? token)
    {
        if (token?.Split('.') is not [_, var payload, _])
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(payload));
            return document.RootElement.ValueKind is JsonValueKind.Object
                && document.RootElement.TryGetProperty(AuthClaim, out var auth) && auth.ValueKind is JsonValueKind.Object
                && auth.TryGetProperty(AccountClaim, out var account) && account.ValueKind is JsonValueKind.String
                && account.GetString() is { Length: > 0 } id
                ? id
                : null;
        }
        // A token that is not a JSON web token names no account; nothing of it is kept.
#pragma warning disable DRV0005
        catch (Exception unreadable) when (unreadable is FormatException or JsonException)
        {
            return null;
        }
#pragma warning restore DRV0005
    }

    /// <summary>When a token says it runs out (<c>exp</c>), or null when it does not say.</summary>
    public static DateTimeOffset? ExpiryOf(string? token)
    {
        if (token?.Split('.') is not [_, var payload, _])
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(payload));
            return document.RootElement.ValueKind is JsonValueKind.Object
                && document.RootElement.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
#pragma warning disable DRV0005
        catch (Exception unreadable) when (unreadable is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
#pragma warning restore DRV0005
    }

    #endregion
}
