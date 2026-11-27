namespace Drover.Adapters.Anthropic;

/// <summary>
/// The provider's own bounds that this adapter writes into each model's record, so the engine knows them without
/// naming a provider. Source (read 2026-11-27): https://platform.claude.com/docs/en/build-with-claude/pdf-support,
/// "Check PDF requirements": a request of at most 128 MB on the first-party API, and at most 600 pages a request, or 100
/// when the request's context window is under 1M tokens.
/// </summary>
internal static class AnthropicLimits
{
    #region Properties

    /// <summary>The largest request the provider takes, in bytes.</summary>
    public const long MaxRequestBytes = 128L * 1024 * 1024;

    /// <summary>
    /// The most bytes of one PDF this adapter sends. A PDF travels as base64, which is four bytes for every three, so a
    /// file of this size fills the request with nothing to spare; anything larger goes to the model as its text.
    /// </summary>
    public const long MaxPdfBytes = MaxRequestBytes / 4 * 3;

    /// <summary>The most pages of PDFs in one request to a model with a context window of 1M tokens or more.</summary>
    public const int MaxPdfPages = 600;

    /// <summary>The most pages of PDFs in one request to a model with a smaller or unknown context window.</summary>
    public const int MaxPdfPagesBelowLargeContext = 100;

    /// <summary>The context window from which a model takes <see cref="MaxPdfPages"/>.</summary>
    public const int LargeContextTokens = 1_000_000;

    #endregion

    #region Public Methods

    /// <summary>The page bound for a model with this context window. An unknown window takes the smaller bound.</summary>
    public static int PdfPagesFor(int? contextTokens) =>
        contextTokens >= LargeContextTokens ? MaxPdfPages : MaxPdfPagesBelowLargeContext;

    #endregion
}
