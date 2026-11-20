namespace Drover.Contracts.Files;

/// <summary>
/// Bounded content returned for an in-app native file preview. The server inspects the stored bytes and returns at
/// most one format payload or a <see cref="NativePreviewRefusal"/>.
/// </summary>
public sealed record NativeFilePreview(
    string? Markdown = null,
    NativePreviewRefusal? Refusal = null,
    byte[]? Image = null,
    IReadOnlyList<NativePdfPage>? Pages = null,
    NativeDiagram? Diagram = null)
{
    #region Constants

    public const int MaxImageBytes = 1024 * 1024;

    public const int MaxImagePixels = 4_000_000;

    public const int MaxTextBytes = 16 * 1024;

    public const int MaxPdfBytes = 1024 * 1024;

    public const int MaxPdfDecodedStreamBytes = 1024 * 1024;

    public const int MaxPdfTotalDecodedBytes = 2 * 1024 * 1024;

    public const int MaxPdfStreams = 64;

    public const int MaxPdfObjects = 256;

    public const int MaxPdfPages = 10;

    public const int MaxPdfPageImages = 4;

    public const int MaxPdfCharacters = 16 * 1024;

    public const int MaxConcurrentPdfPreviews = 2;

    public const int MaxPdfPreviewsPerUserPerMinute = 10;

    public const int MaxDiagramNodes = 32;

    public const int MaxDiagramEdges = 64;

    public const int MaxDiagramLabelLength = 120;

    #endregion
}

/// <summary>One page of a bounded PDF preview.</summary>
public sealed record NativePdfPage(int Number, string Text, byte[]? Image = null);

/// <summary>A bounded directed graph parsed from a diagram file or fence.</summary>
public sealed record NativeDiagram(
    string Direction,
    IReadOnlyList<NativeDiagramNode> Nodes,
    IReadOnlyList<NativeDiagramEdge> Edges);

/// <summary>One node in a <see cref="NativeDiagram"/>.</summary>
public sealed record NativeDiagramNode(string Id, string Label);

/// <summary>One directed connection in a <see cref="NativeDiagram"/>.</summary>
public sealed record NativeDiagramEdge(string From, string To, string? Label = null);

/// <summary>Why a file cannot be shown in a native preview.</summary>
public enum NativePreviewRefusal
{
    Unsupported,
    TooLarge,
    Malformed,
    TooComplex,
}
