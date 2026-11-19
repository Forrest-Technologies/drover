namespace Drover.Domain.Web;

/// <summary>
/// One search. <see cref="MaxResponseBytes"/> is how much of the service's answer Drover will read; an answer past it
/// is refused as <see cref="WebSearchFailure.TooLarge"/>, not cut.
/// </summary>
public sealed record WebSearchRequest(string Query, int MaxResults, long MaxResponseBytes);
