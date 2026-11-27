namespace Drover.Engine.Models;

/// <summary>
/// Turns what Azure's portal shows into the v1 address the compatible adapter calls: the bare resource endpoint, or
/// a classic deployment address with an <c>api-version</c> query, become <c>https://{host}/openai/v1/</c>
/// (https://learn.microsoft.com/en-us/azure/foundry/openai/api-version-lifecycle; the deployment name is the
/// <c>model</c> value, https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/endpoints). It is one
/// of the two places that know Azure's host suffixes; the other is the adapter's key header. Anything it does not
/// recognise is returned unchanged, so the address check still refuses a query, credentials or a fragment.
/// </summary>
public static class AzureAddress
{
    #region Private Fields

    private static readonly string[] hostSuffixes = [".openai.azure.com", ".services.ai.azure.com"];

    #endregion

    #region Public Methods

    public static (string? Address, string? DefaultModel) Normalize(string? address, string? defaultModel)
    {
        if (address is null
            || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !hostSuffixes.Any(suffix => uri.IdnHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            || uri.UserInfo.Length > 0
            || uri.Fragment.Length > 0
            || !OnlyApiVersion(uri.Query))
        {
            return (address, defaultModel);
        }

        var v1 = $"https://{uri.Authority}/openai/v1/";
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments is ["openai", "v1"])
        {
            // A query on the endpoint or on the v1 address is not Azure's to explain: leave it to the refusal.
            return uri.Query.Length > 0 ? (address, defaultModel) : (v1, defaultModel);
        }

        if (segments is ["openai", "deployments", { Length: > 0 } name, ..])
        {
            return (v1, string.IsNullOrWhiteSpace(defaultModel) ? Uri.UnescapeDataString(name) : defaultModel);
        }

        return (address, defaultModel);
    }

    #endregion

    #region Private Methods

    private static bool OnlyApiVersion(string query) =>
        query.Length == 0
        || query.TrimStart('?').Split('&').All(pair => pair.StartsWith("api-version=", StringComparison.OrdinalIgnoreCase));

    #endregion
}
