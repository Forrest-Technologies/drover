namespace Drover.Engine.Files;

/// <summary>
/// Inspects stored file bytes and reads bounded content for the native client's Markdown, static image, PDF, and
/// diagram viewers.
/// </summary>
public static class NativePreviewReader
{
    #region Public Methods

    public static async Task<NativeFilePreview> Read(
        Stream content,
        CancellationToken cancellationToken,
        Guid userId = default,
        TimeSpan? pdfDeadline = null,
        Action? onPdfParse = null)
    {
        var prefix = new byte[12];
        var prefixLength = await content.ReadAtLeastAsync(prefix, prefix.Length, false, cancellationToken);
        var slice = prefix.AsSpan(0, prefixLength);
        var image = IsStaticImageCandidate(slice);
        var pdf = !image && IsPdfCandidate(slice);
        var limit = image
            ? NativeFilePreview.MaxImageBytes
            : pdf
                ? NativeFilePreview.MaxPdfBytes
                : NativeFilePreview.MaxTextBytes;
        var buffer = new byte[limit + 1];
        slice.CopyTo(buffer);
        var read = await content.ReadAtLeastAsync(
            buffer.AsMemory(prefixLength), buffer.Length - prefixLength, false, cancellationToken);
        var length = prefixLength + read;
        if (length > limit)
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        var bytes = buffer.AsSpan(0, length);
        if (image)
        {
            return InspectImage(bytes);
        }

        if (pdf)
        {
            return await PdfPreviewGuard.Run(bytes.ToArray(), userId, pdfDeadline, onPdfParse, cancellationToken);
        }

        return InspectText(bytes);
    }

    #endregion

    #region Private Methods

    private static bool IsStaticImageCandidate(ReadOnlySpan<byte> bytes) =>
        bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] or [0xFF, 0xD8, 0xFF, ..];

    private static bool IsPdfCandidate(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith("%PDF-"u8);

    private static NativeFilePreview InspectImage(ReadOnlySpan<byte> bytes)
    {
        var inspector = new FileInspector();
        inspector.Append(bytes);
        var facts = inspector.Finish(string.Empty);
        if (facts is not { Kind: FileKind.Image, Width: int width, Height: int height })
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        return (long)width * height > NativeFilePreview.MaxImagePixels
            ? new(Refusal: NativePreviewRefusal.TooLarge)
            : new(Image: bytes.ToArray());
    }

    private static NativeFilePreview InspectText(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Contains((byte)0))
        {
            return new(Refusal: NativePreviewRefusal.Unsupported);
        }

        if (bytes.StartsWith("\uFEFF"u8))
        {
            bytes = bytes[3..];
        }

        var chars = new char[bytes.Length];
        var status = Utf8.ToUtf16(bytes, chars, out _, out var written, replaceInvalidSequences: false);
        if (status is not OperationStatus.Done)
        {
            return new(Refusal: NativePreviewRefusal.Malformed);
        }

        var text = new string(chars, 0, written);
        if (text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
        {
            return new(Refusal: NativePreviewRefusal.Unsupported);
        }

        if (NativeDiagramParser.IsDiagramText(text))
        {
            return NativeDiagramParser.TryParse(text, out var diagram, out var refusal) && diagram is not null
                ? new(Diagram: diagram)
                : new(Refusal: refusal ?? NativePreviewRefusal.Malformed);
        }

        if (!NativeDiagramParser.ValidateMarkdownFences(text, out var fenceRefusal))
        {
            return new(Refusal: fenceRefusal ?? NativePreviewRefusal.Malformed);
        }

        return new(Markdown: text);
    }

    #endregion
}
