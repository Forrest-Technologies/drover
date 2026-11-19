namespace Drover.Adapters.SearXng;

/// <summary>
/// The operator's own search service: a SearXNG instance with its JSON output turned on (<c>search.formats</c> lists
/// <c>json</c> in its settings). It needs no account and no key.
/// </summary>
public sealed class SearXngOptions
{
    #region Properties

    /// <summary>The address the instance answers on, such as <c>http://searxng:8080/</c>.</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:8080/");

    #endregion
}
