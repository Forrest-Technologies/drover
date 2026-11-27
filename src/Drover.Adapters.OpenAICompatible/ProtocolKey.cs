namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// How a key travels on the compatible wire, in one place: <c>Authorization: Bearer</c> everywhere, and on Azure's own
/// hosts also the <c>api-key</c> header. Azure's v1 page shows the key through a client library and, as a raw request,
/// in <c>api-key</c> (https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle), so sending both
/// means nobody has to find out which form their resource takes. The host check is one of the two places that know
/// Azure's host suffixes; <c>AzureAddress</c> in the engine is the other.
/// </summary>
internal static class ProtocolKey
{
    #region Private Fields

    private static readonly string[] azureHostSuffixes = [".openai.azure.com", ".services.ai.azure.com"];

    #endregion

    #region Public Methods

    /// <summary>Every header that carries the key to this address. Empty when there is no key.</summary>
    public static IReadOnlyDictionary<string, string> Headers(Uri address, string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return new Dictionary<string, string>();
        }

        var headers = new Dictionary<string, string>(ExtraHeaders(address, apiKey)) { ["Authorization"] = $"Bearer {apiKey}" };
        return headers;
    }

    /// <summary>The headers a call adds on top of the bearer token its client library already sends.</summary>
    public static IReadOnlyDictionary<string, string> ExtraHeaders(Uri address, string apiKey)
    {
        Dictionary<string, string> headers = [];

        if (IsAzure(address))
        {
            headers["api-key"] = apiKey;
        }

        return headers;
    }

    public static void Apply(HttpRequestMessage request, Uri address, string? apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return;
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        foreach (var (name, value) in ExtraHeaders(address, apiKey))
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    #endregion

    #region Private Methods

    public static bool IsAzure(Uri address) =>
        azureHostSuffixes.Any(suffix => address.IdnHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    #endregion
}
