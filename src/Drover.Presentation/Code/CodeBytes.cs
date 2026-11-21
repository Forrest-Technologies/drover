namespace Drover.Presentation.Code;

/// <summary>
/// Text that is really bytes. Output a program wrote as bytes reads as noise once decoded as text, and a bytes
/// literal (<c>b'h\x1c\xfeK'</c>) as escapes. Either is summarized by its size and shown on demand as offset, hex,
/// and printable characters.
/// </summary>
public static class CodeBytes
{
    #region Private Fields

    /// <summary>The bytes on one line of the hex view.</summary>
    public const int BytesPerLine = 16;

    // Past one control character in a hundred, decoded text is bytes.
    private const int ControlShare = 100;

    // Past one replacement character in ten, the bytes were not text to begin with.
    private const int ReplacementShare = 10;

    // A bytes literal is most of the text: what is around it may be no more than a tenth.
    private const int LiteralShare = 10;

    #endregion

    #region Public Methods

    /// <summary>
    /// True when decoded text holds bytes rather than words: a NUL, more control characters than a terminal's colors,
    /// tabs, and line breaks explain, or more than one character in ten that could not be decoded.
    /// </summary>
    public static bool IsBinary(string text)
    {
        var controls = 0;
        var replacements = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];

            if (character is '\0')
            {
                return true;
            }

            if (character is '�')
            {
                replacements++;
            }
            else if (char.IsControl(character) && !IsTextControl(text, index))
            {
                controls++;
            }
        }

        return controls * ControlShare > text.Length || replacements * ReplacementShare > text.Length;
    }

    /// <summary>
    /// The bytes of a bytes literal that is most of the text, <c>b'...'</c> or <c>b"..."</c> with at least one
    /// <c>\xNN</c> escape, or nothing when the text is not one.
    /// </summary>
    public static byte[]? Literal(string text)
    {
        var trimmed = text.Trim();

        if (LiteralIn(trimmed) is not var (start, end) || (trimmed.Length - (end - start)) * LiteralShare > trimmed.Length)
        {
            return null;
        }

        var quote = trimmed[start + 1];
        var body = trimmed[(start + 2)..(end - 1)];
        return body.Contains("\\x", StringComparison.Ordinal) ? Unescaped(body, quote) : null;
    }

    /// <summary>The bytes as the hex view shows them, <see cref="BytesPerLine"/> to a line.</summary>
    public static IReadOnlyList<HexLine> HexLines(IReadOnlyList<byte> bytes) =>
        [.. Enumerable.Range(0, (bytes.Count + BytesPerLine - 1) / BytesPerLine).Select(line => HexLineAt(bytes, line * BytesPerLine))];

    /// <summary>A size in words: "1 byte", "2,048 bytes".</summary>
    public static string Size(int bytes) => $"{bytes.ToString("N0", CultureInfo.InvariantCulture)} {(bytes == 1 ? "byte" : "bytes")}";

    #endregion

    #region Private Methods

    // Tabs, line breaks, a backspace, form feed or bell in progress output, and the escape that starts a terminal color are
    // text a program writes on purpose.
    private static bool IsTextControl(string text, int index) => text[index] switch
    {
        '\t' or '\n' or '\r' or '\b' or '\f' or '\a' => true,
        '\u001b' => index + 1 < text.Length && text[index + 1] is '[' or ']',
        _ => false,
    };

    // The escapes a bytes literal may hold; any other character must be printable ASCII.
    private static byte[]? Unescaped(string body, char quote)
    {
        List<byte> bytes = [];

        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];

            if (character is not '\\')
            {
                if (character is < ' ' or > '~' || character == quote)
                {
                    return null;
                }

                bytes.Add((byte)character);
                continue;
            }

            if (++index >= body.Length)
            {
                return null;
            }

            if (body[index] is 'x')
            {
                if (index + 2 >= body.Length
                    || !byte.TryParse(body.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
                {
                    return null;
                }

                bytes.Add(value);
                index += 2;
                continue;
            }

            bytes.AddRange(body[index] switch
            {
                'n' => [(byte)'\n'],
                't' => [(byte)'\t'],
                'r' => [(byte)'\r'],
                '0' => [0],
                'a' => [7],
                'b' => [8],
                'f' => [12],
                'v' => [11],
                '\\' or '\'' or '"' => [(byte)body[index]],
                _ => [(byte)'\\', (byte)body[index]],
            });
        }

        return [.. bytes];
    }

    private static HexLine HexLineAt(IReadOnlyList<byte> bytes, int offset)
    {
        var count = Math.Min(BytesPerLine, bytes.Count - offset);
        var hex = new StringBuilder();
        var printable = new StringBuilder();

        for (var index = 0; index < count; index++)
        {
            var value = bytes[offset + index];
            hex.Append(index switch
            {
                0 => string.Empty,
                BytesPerLine / 2 => "  ",
                _ => " ",
            });
            hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            printable.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
        }

        return new(offset.ToString("x8", CultureInfo.InvariantCulture), hex.ToString(), printable.ToString());
    }

    // Where the first bytes literal stands, from its b to just past its closing quote, found in one pass: a backslash
    // and the character after it are passed over together, so the cost is the length of the text whatever it holds.
    // A literal that is most of the text starts in its first tenth; one that is never closed is none.
    private static (int Start, int End)? LiteralIn(string text)
    {
        var reach = text.Length / LiteralShare;
        var start = -1;

        for (var index = 0; index + 1 < text.Length && index <= reach; index++)
        {
            if (text[index] is 'b' && text[index + 1] is '\'' or '"' && (index == 0 || !char.IsLetterOrDigit(text[index - 1])))
            {
                start = index;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        var quote = text[start + 1];

        for (var index = start + 2; index < text.Length; index++)
        {
            if (text[index] == quote)
            {
                return (start, index + 1);
            }

            if (text[index] is '\\')
            {
                index++;
            }
        }

        return null;
    }

    #endregion
}

/// <summary>One line of the hex view: the offset of its first byte, its bytes in hex, and their printable characters.</summary>
public sealed record HexLine(string Offset, string Hex, string Printable);
