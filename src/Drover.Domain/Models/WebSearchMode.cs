namespace Drover.Domain.Models;

/// <summary>A bot's choice about a provider's built-in web search. Separate from Drover's own search tool.</summary>
public enum WebSearchMode
{
    Off,
    On,
    Auto,
}
