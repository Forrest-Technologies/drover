namespace Drover.Adapters.OpenAICompatible;

/// <summary>The Copilot chat API address, always with a trailing slash so relative paths join.</summary>
internal static class CopilotApiAddress
{
    #region Public Methods

    public static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/");

    #endregion
}
