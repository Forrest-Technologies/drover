namespace Drover.Domain.Web;

/// <summary>
/// A search failed. The message is the adapter's own plain words for the person and the model. It never holds the
/// service's answer, a key, or the query.
/// </summary>
public sealed class WebSearchException(WebSearchFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public WebSearchFailure Failure { get; } = failure;
}
