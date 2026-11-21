namespace Drover.Domain.Web;

/// <summary>
/// Searches the web for a bot. The operator configures one search service for the server; a bot reaches it only
/// through the <c>web.search</c> tool, after its grant and the tool gate. What comes back is a third party's text and
/// is treated as untrusted wherever it goes.
/// </summary>
public interface IWebSearch
{
    /// <summary>The adapter's name, such as <c>searxng</c>, for logs and the status page.</summary>
    string Kind { get; }

    /// <summary>
    /// Runs one search and returns at most <see cref="WebSearchRequest.MaxResults"/> results, best first. A service
    /// that cannot answer throws <see cref="WebSearchException"/>; an empty list is a search that found nothing.
    /// Cancellation throws; it is never reported as a failure.
    /// </summary>
    Task<IReadOnlyList<WebSearchResult>> Search(WebSearchRequest request, CancellationToken cancellationToken);
}
