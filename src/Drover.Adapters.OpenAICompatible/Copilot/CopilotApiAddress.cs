namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Reads the Copilot API host from a session token's <c>proxy-ep</c> claim. Individual accounts use
/// <c>api.individual.githubcopilot.com</c>; a business or enterprise token names another host.
/// </summary>
internal static class CopilotApiAddress
{
    #region Private Fields

    private static readonly Regex proxyEndpoint = new(@"proxy-ep=([^;]+)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    #endregion

    #region Public Methods

    public static Uri? FromToken(string token)
    {
        var match = proxyEndpoint.Match(token);

        if (!match.Success)
        {
            return null;
        }

        var host = match.Groups[1].Value.Trim();

        if (host.Length == 0)
        {
            return null;
        }

        var apiHost = host.StartsWith("proxy.", StringComparison.OrdinalIgnoreCase)
            ? $"api.{host["proxy.".Length..]}"
            : host;

        return Uri.TryCreate($"https://{apiHost}/", UriKind.Absolute, out var address) ? address : null;
    }

    public static Uri Resolve(string? accessToken, IReadOnlyDictionary<string, string> details, Uri fallback)
    {
        if (accessToken is { Length: > 0 } && FromToken(accessToken) is { } fromToken)
        {
            return fromToken;
        }

        return details.TryGetValue("api", out var stored) && Uri.TryCreate(stored, UriKind.Absolute, out var fromDetails)
            ? WithTrailingSlash(fromDetails)
            : WithTrailingSlash(fallback);
    }

    public static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/");

    #endregion
}
