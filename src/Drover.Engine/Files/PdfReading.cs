namespace Drover.Engine.Files;

/// <summary>
/// How a model that reads PDFs as PDFs may be given one: at most <paramref name="MaxPages"/> pages and
/// <paramref name="MaxBytes"/> bytes, the smaller of the server's bounds and the provider's for the model.
/// </summary>
public sealed record PdfReading(int MaxPages, long MaxBytes)
{
    #region Public Methods

    /// <summary>
    /// The bounds for a model with these capabilities, or null when its catalog entry does not say it reads PDFs. The
    /// provider's bounds come from its adapter through the catalog, so no provider is named here.
    /// </summary>
    public static PdfReading? For(ModelCapabilities? capabilities, int maxDocumentPages, long maxFileBytes) =>
        capabilities is { PdfInput: true }
            ? new(
                Math.Min(maxDocumentPages, capabilities.PdfMaxPages ?? int.MaxValue),
                Math.Min(maxFileBytes, capabilities.PdfMaxBytes ?? long.MaxValue))
            : null;

    #endregion
}
