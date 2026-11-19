namespace Drover.Domain.Web;

/// <summary>One result: the page's title, its address, and the service's short excerpt of it. All three are untrusted text.</summary>
public sealed record WebSearchResult(string Title, Uri Url, string Snippet);
