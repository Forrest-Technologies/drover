namespace Drover.Ui.Tests.Rules;

/// <summary>
/// Small text helpers shared by the style and markup readers. Masking replaces characters with spaces
/// and keeps line breaks, so offsets and line numbers stay true to the file.
/// </summary>
public static class SourceText
{
    #region Public Methods

    public static int LineAt(string text, int offset) =>
        text.AsSpan(0, Math.Clamp(offset, 0, text.Length)).Count('\n') + 1;

    public static string Blanked(string value) =>
        string.Concat(value.Select(character => character is '\n' or '\r' ? character : ' '));

    public static string BlankMatches(string text, Regex pattern) =>
        pattern.Replace(text, match => Blanked(match.Value));

    public static string BlankRange(string text, int start, int end) =>
        $"{text[..start]}{Blanked(text[start..end])}{text[end..]}";

    /// <summary>
    /// Blanks every balanced group whose opening is matched by <paramref name="opener"/>.
    /// The opener's last character must be the opening bracket.
    /// </summary>
    public static string BlankBalanced(string text, Regex opener, char open, char close)
    {
        var masked = text;
        var match = opener.Match(masked);
        while (match.Success)
        {
            var end = SkipBalanced(masked, match.Index + match.Length - 1, open, close);
            masked = BlankRange(masked, match.Index, end);
            match = opener.Match(masked, end);
        }

        return masked;
    }

    /// <summary>
    /// Returns the index after the string that opens at <paramref name="quoteIndex"/>. A line break ends it.
    /// </summary>
    public static int SkipString(string text, int quoteIndex)
    {
        var quote = text[quoteIndex];
        var index = quoteIndex + 1;
        while (index < text.Length && text[index] != quote && text[index] is not '\n')
        {
            index += text[index] is '\\' ? 2 : 1;
        }

        return Math.Min(index + 1, text.Length);
    }

    /// <summary>
    /// Returns the index after the bracket that closes the one at <paramref name="openIndex"/>.
    /// </summary>
    public static int SkipBalanced(string text, int openIndex, char open, char close)
    {
        var depth = 0;
        var index = openIndex;
        while (index < text.Length)
        {
            var character = text[index];
            if (character is '"' or '\'')
            {
                index = SkipString(text, index);
                continue;
            }

            if (character == open)
            {
                depth++;
            }
            else if (character == close && --depth == 0)
            {
                return index + 1;
            }

            index++;
        }

        return text.Length;
    }

    #endregion
}
