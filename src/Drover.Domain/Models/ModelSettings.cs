namespace Drover.Domain.Models;

/// <summary>Per-bot choices that change how a provider is called. Reasoning effort travels on the chat options.</summary>
public sealed record ModelSettings(WebSearchMode WebSearch = WebSearchMode.Off);
