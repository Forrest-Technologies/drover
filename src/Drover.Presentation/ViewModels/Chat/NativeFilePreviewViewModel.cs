namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// Bounded native preview state for a shared file: Markdown nodes, static image bytes, PDF pages with keyboard-friendly
/// page navigation, or a flowchart diagram model. Closing the preview releases all loaded preview state.
/// </summary>
public sealed class NativeFilePreviewViewModel(ILogger<NativeFilePreviewViewModel>? logger = null) : ObservableObject
{
    #region Private Fields

    private CancellationTokenSource? pending;

    private SharedFile? file;

    private int pageIndex;

    #endregion

    #region Properties

    public SharedFile? File
    {
        get => file;
        set
        {
            if (file != value)
            {
                Close();
                file = value;
            }

            Changed();
        }
    }

    /// <summary>Reads bounded, validated native preview data for a shared file.</summary>
    public Func<SharedFile, CancellationToken, Task<NativeFilePreview>>? ReadPreview { get; set; }

    public bool IsSupported =>
        file is not null
        && !(file.FromBot && file.ContentType.Equals("text/html", StringComparison.OrdinalIgnoreCase));

    public bool Open { get; private set; }

    public bool Loading { get; private set; }

    public string ButtonLabel => Open ? "Close preview" : "Preview";

    public string ButtonName =>
        file is null
            ? "Preview"
            : Open
                ? $"Close preview of {file.Name}"
                : $"Preview {file.Name}";

    public string? Status { get; private set; }

    public IReadOnlyList<MarkdownNode> MarkdownNodes { get; private set; } = [];

    public byte[]? ImageBytes { get; private set; }

    public IReadOnlyList<NativePdfPage> PdfPages { get; private set; } = [];

    public int PageIndex => pageIndex;

    public NativePdfPage? CurrentPdfPage =>
        pageIndex >= 0 && pageIndex < PdfPages.Count ? PdfPages[pageIndex] : null;

    public string? PageCaption =>
        CurrentPdfPage is { } page ? $"Page {page.Number} of {PdfPages.Count}" : null;

    public bool CanPreviousPage => pageIndex > 0;

    public bool CanNextPage => pageIndex >= 0 && pageIndex + 1 < PdfPages.Count;

    public NativeDiagram? Diagram { get; private set; }

    #endregion

    #region Public Methods

    public async Task Toggle()
    {
        if (Open)
        {
            Close();
            return;
        }

        if (file is not { } target || !IsSupported || ReadPreview is null)
        {
            return;
        }

        Open = true;
        Loading = true;
        Status = "Loading preview.";
        Changed();

        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        pending = request;
        try
        {
            var preview = await ReadPreview(target, request.Token);
            if (pending == request)
            {
                Apply(preview);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception)
            || exception is OperationCanceledException or ArgumentException or InvalidOperationException or IOException)
        {
            logger?.LogWarning(exception, "The native file preview could not be shown");
            if (pending == request)
            {
                Status = "Could not show the preview. Download the file to keep it.";
            }
        }
        finally
        {
            if (pending == request)
            {
                pending = null;
                Loading = false;
                Changed();
            }
        }
    }

    public void PreviousPage()
    {
        if (!CanPreviousPage)
        {
            return;
        }

        pageIndex--;
        Changed();
    }

    public void NextPage()
    {
        if (!CanNextPage)
        {
            return;
        }

        pageIndex++;
        Changed();
    }

    public void FailImageDecode()
    {
        ImageBytes = null;
        Status = "Could not decode this picture. Download the file to keep it.";
        Changed();
    }

    public void Close()
    {
        var request = pending;
        pending = null;
        request?.Cancel();
        Open = false;
        Loading = false;
        Status = null;
        MarkdownNodes = [];
        ImageBytes = null;
        PdfPages = [];
        pageIndex = 0;
        Diagram = null;
        Changed();
    }

    public static string RefusalSentence(NativePreviewRefusal? refusal) => refusal switch
    {
        NativePreviewRefusal.TooLarge =>
            "Preview limits are 16 KiB for text, 1 MiB and 4 million pixels for pictures, and 1 MiB and 10 pages for PDF files. Download the file to keep it.",
        NativePreviewRefusal.Malformed =>
            "This file contains invalid preview data. Download it to keep it.",
        NativePreviewRefusal.Unsupported =>
            "This file format has no native preview. Download it to keep it.",
        _ =>
            "This file exceeds the preview complexity limit. Download it to keep it.",
    };

    #endregion

    #region Private Methods

    private void Apply(NativeFilePreview preview)
    {
        MarkdownNodes = [];
        ImageBytes = null;
        PdfPages = [];
        pageIndex = 0;
        Diagram = null;

        if (preview is { Refusal: null, Image: { Length: > 0 } bytes })
        {
            ImageBytes = bytes;
            Status = null;
            return;
        }

        if (preview is { Refusal: null, Diagram: { } diagram })
        {
            Diagram = diagram;
            Status = null;
            return;
        }

        if (preview is { Refusal: null, Pages: { Count: > 0 } pages })
        {
            PdfPages = pages;
            pageIndex = 0;
            Status = null;
            return;
        }

        if (preview is { Refusal: null, Markdown: { } text })
        {
            if (FileMarkdown.Parse(text) is { } nodes)
            {
                MarkdownNodes = nodes;
                Status = null;
            }
            else
            {
                Status = RefusalSentence(NativePreviewRefusal.TooComplex);
            }

            return;
        }

        Status = RefusalSentence(preview.Refusal);
    }

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
