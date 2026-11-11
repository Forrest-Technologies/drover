namespace Drover.Adapters.Venice;

/// <summary>
/// Builds the documented model suffix form, <c>model:key=value&amp;key=value</c>, so no custom request body is needed.
/// Every key used here is listed in Venice's model feature suffix documentation.
/// </summary>
internal static class VeniceModelSuffix
{
    #region Public Methods

    /// <summary>The provider's own system prompt is always switched off: a bot's instructions are fully the user's.</summary>
    public static string Build(string modelId, WebSearchMode webSearch, bool disableThinking)
    {
        List<string> options = ["include_venice_system_prompt=false"];

        if (WebSearchValue(webSearch) is { } search)
        {
            options.Add($"enable_web_search={search}");
        }

        if (disableThinking)
        {
            options.Add("disable_thinking=true");
        }

        return $"{modelId}:{string.Join('&', options)}";
    }

    #endregion

    #region Private Methods

    private static string? WebSearchValue(WebSearchMode mode) =>
        mode switch
        {
            WebSearchMode.On => "on",
            WebSearchMode.Auto => "auto",
            _ => null,
        };

    #endregion
}
