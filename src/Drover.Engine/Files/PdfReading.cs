namespace Drover.Engine.Files;

/// <summary>
/// How a model that reads PDFs as PDFs may be given them: at most <paramref name="MaxPages"/> pages and
/// <paramref name="MaxBytes"/> bytes in one model call, the smaller of the server's bounds and the provider's for the
/// model.
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


/// <summary>
/// What is left of <see cref="PdfReading"/> for one model call. The provider's bounds are for a whole request, so
/// each PDF given as itself takes its pages and bytes from here, and one that no longer fits takes the text path.
/// </summary>
public sealed class PdfAllowance(PdfReading reading)
{
    #region Properties

    public int Pages { get; private set; } = reading.MaxPages;

    public long Bytes { get; private set; } = reading.MaxBytes;

    #endregion

    #region Public Methods

    /// <summary>Takes a PDF's pages and bytes when both fit, and says whether they did.</summary>
    public bool TryTake(int pages, long bytes)
    {
        if (pages > Pages || bytes > Bytes)
        {
            return false;
        }

        Pages -= pages;
        Bytes -= bytes;
        return true;
    }

    #endregion
}
