namespace Drover.Domain.Files;

/// <summary>What the server found a file to be: its kind, the content type it is kept as, and how it is shown.</summary>
public sealed record FileFacts(FileKind Kind, string ContentType, int? Width = null, int? Height = null, string? Preview = null);

/// <summary>
/// Reads a file as it streams past, once, and says what it is. A picture is a PNG, JPEG, GIF, or WebP whose header
/// gives its size; text is UTF-8 with no NUL byte anywhere in it; a document is a PDF by its header, or a Word, Excel,
/// or PowerPoint file, which is a zip archive by its header and told apart by the name's extension. The content type
/// is chosen here from that, and from the name's extension only to tell kinds of text and of office document apart, so
/// a content type always names the kind it belongs to (<see cref="KindOf"/>). Nothing here trusts what the browser
/// said the file was.
/// </summary>
public sealed class FileInspector
{
    #region Private Fields

    /// <summary>How much of the start is kept: enough for any header Drover reads, and a text file's first lines.</summary>
    private const int HeadLength = 256 * 1024;

    private const int LargestSide = 30_000;

    private const int PreviewLines = 6;

    private const int PreviewCharacters = 500;

    private static readonly Dictionary<string, string> textTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".md"] = "text/markdown",
        [".markdown"] = "text/markdown",
        [".csv"] = "text/csv",
        [".tsv"] = "text/tab-separated-values",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".yaml"] = "application/yaml",
        [".yml"] = "application/yaml",
        [".html"] = "text/html",
        [".htm"] = "text/html",
    };

    private static readonly Dictionary<string, string> officeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".docx"] = DocumentTypes.Word,
        [".xlsx"] = DocumentTypes.Excel,
        [".pptx"] = DocumentTypes.PowerPoint,
    };

    private static readonly Dictionary<string, string> otherTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".zip"] = "application/zip",
        [".gz"] = "application/gzip",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
    };

    // The inspector writes every content type in lowercase, so these compare exactly.
    private static readonly HashSet<string> imageTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    private static readonly HashSet<string> textLikeTypes = ["application/json", "application/xml", "application/yaml"];

    private const int CharBufferLength = 4096;

    private readonly byte[] head = new byte[HeadLength];

    // The last bytes of a chunk that begin a character the next chunk finishes.
    private byte[] carry = [];

    private int headLength;

    private bool isText = true;

    #endregion

    #region Public Methods

    /// <summary>Takes the next bytes of the file.</summary>
    public void Append(ReadOnlySpan<byte> bytes)
    {
        var room = Math.Min(bytes.Length, HeadLength - headLength);
        bytes[..room].CopyTo(head.AsSpan(headLength));
        headLength += room;

        if (isText)
        {
            isText = bytes.IndexOf((byte)0) < 0 && IsUtf8(bytes, flush: false);
        }
    }

    /// <summary>What the file is, once every byte has been appended. The name only tells kinds of text apart.</summary>
    public FileFacts Finish(string name)
    {
        var start = head.AsSpan(0, headLength);

        if (FindImage(start) is { } image)
        {
            return image;
        }

        var extension = Path.GetExtension(name);

        // Before text: a PDF may be all ASCII, and is still a PDF.
        if (FindDocument(start, extension) is { } document)
        {
            return new FileFacts(FileKind.Document, document);
        }

        if (isText && IsUtf8([], flush: true))
        {
            return new FileFacts(FileKind.Text, textTypes.GetValueOrDefault(extension, "text/plain"), Preview: PreviewOf(start));
        }

        return new FileFacts(FileKind.Other, otherTypes.GetValueOrDefault(extension, "application/octet-stream"));
    }

    /// <summary>The kind a content type the inspector chose belongs to.</summary>
    public static FileKind KindOf(string contentType) =>
        imageTypes.Contains(contentType) ? FileKind.Image
        : contentType.StartsWith("text/", StringComparison.Ordinal) || textLikeTypes.Contains(contentType) ? FileKind.Text
        : DocumentTypes.All.Contains(contentType) ? FileKind.Document
        : FileKind.Other;

    #endregion

    #region Text

    /// <summary>
    /// Whether the bytes so far are UTF-8. A character cut in two by the end of a chunk is carried to the next one;
    /// at the end of the file (<paramref name="flush"/>) nothing may be left over.
    /// </summary>
    private bool IsUtf8(ReadOnlySpan<byte> bytes, bool flush)
    {
        var input = carry.Length == 0 ? bytes : Joined(carry, bytes);
        Span<char> chars = stackalloc char[CharBufferLength];

        while (true)
        {
            var status = Utf8.ToUtf16(input, chars, out var read, out _, replaceInvalidSequences: false, isFinalBlock: flush);
            input = input[read..];

            switch (status)
            {
                case OperationStatus.DestinationTooSmall:
                    continue;
                case OperationStatus.NeedMoreData:
                    carry = input.ToArray();
                    return true;
                case OperationStatus.Done:
                    carry = [];
                    return true;
                default:
                    return false;
            }
        }
    }

    private static byte[] Joined(byte[] first, ReadOnlySpan<byte> second) => [.. first, .. second];

    /// <summary>The first few lines, cut at a whole character, without a trailing line break.</summary>
    private static string PreviewOf(ReadOnlySpan<byte> start)
    {
        var text = new UTF8Encoding(false, false).GetString(start[..Math.Min(start.Length, PreviewCharacters * 4)]);
        var lines = text.ReplaceLineEndings("\n").Split('\n').Take(PreviewLines);
        var preview = string.Join('\n', lines);
        preview = preview.Length > PreviewCharacters ? preview[..PreviewCharacters] : preview;
        return preview.TrimEnd('�').TrimEnd();
    }

    #endregion

    #region Documents

    /// <summary>A PDF by its header, whatever its name; an office file is a zip archive with the extension of one.</summary>
    private static string? FindDocument(ReadOnlySpan<byte> start, string extension) => start switch
    {
        [0x25, 0x50, 0x44, 0x46, 0x2D, ..] => DocumentTypes.Pdf,
        [0x50, 0x4B, 0x03, 0x04, ..] => officeTypes.GetValueOrDefault(extension),
        _ => null,
    };

    #endregion

    #region Pictures

    private static FileFacts? FindImage(ReadOnlySpan<byte> start)
    {
        var found = start switch
        {
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => ("image/png", PngSize(start)),
            [0xFF, 0xD8, 0xFF, ..] => ("image/jpeg", JpegSize(start)),
            [0x47, 0x49, 0x46, 0x38, 0x37 or 0x39, 0x61, ..] => ("image/gif", GifSize(start)),
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => ("image/webp", WebpSize(start)),
            _ => (null, null),
        };

        return found is (string type, ({ } width, { } height))
            && width is > 0 and <= LargestSide && height is > 0 and <= LargestSide
            ? new FileFacts(FileKind.Image, type, width, height)
            : null;
    }

    private static (int, int)? PngSize(ReadOnlySpan<byte> start) =>
        start.Length >= 24 && start[12..16].SequenceEqual("IHDR"u8)
            ? (BigEndian32(start[16..]), BigEndian32(start[20..]))
            : null;

    private static (int, int)? GifSize(ReadOnlySpan<byte> start) =>
        start.Length >= 10 ? (start[6] | (start[7] << 8), start[8] | (start[9] << 8)) : null;

    private static (int, int)? WebpSize(ReadOnlySpan<byte> start)
    {
        if (start.Length < 30)
        {
            return null;
        }

        var chunk = start[12..16];

        if (chunk.SequenceEqual("VP8 "u8) && start[23] == 0x9D && start[24] == 0x01 && start[25] == 0x2A)
        {
            return ((start[26] | (start[27] << 8)) & 0x3FFF, (start[28] | (start[29] << 8)) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8) && start[20] == 0x2F)
        {
            var bits = start[21] | (start[22] << 8) | (start[23] << 16) | (start[24] << 24);
            return ((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            return ((start[24] | (start[25] << 8) | (start[26] << 16)) + 1, (start[27] | (start[28] << 8) | (start[29] << 16)) + 1);
        }

        return null;
    }

    /// <summary>The size in the first start-of-frame segment, walking the segments from the top.</summary>
    private static (int, int)? JpegSize(ReadOnlySpan<byte> start)
    {
        var position = 2;

        while (position + 9 < start.Length)
        {
            if (start[position] != 0xFF)
            {
                return null;
            }

            var marker = start[position + 1];

            if (marker == 0xFF)
            {
                position++;
                continue;
            }

            if (marker is >= 0xD0 and <= 0xD9 or 0x01)
            {
                position += 2;
                continue;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return (BigEndian16(start[(position + 7)..]), BigEndian16(start[(position + 5)..]));
            }

            position += 2 + BigEndian16(start[(position + 2)..]);
        }

        return null;
    }

    private static int BigEndian16(ReadOnlySpan<byte> bytes) => (bytes[0] << 8) | bytes[1];

    private static int BigEndian32(ReadOnlySpan<byte> bytes) =>
        bytes[0] > 0x7F ? 0 : (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    #endregion
}
