namespace Drover.Ui.Session;

/// <summary>
/// The words a system notification may show. A bot name is written by another person, so the sentence is plain
/// text: no control characters, no bidirectional overrides, and no cut through the middle of a character.
/// </summary>
public static class NotificationText
{
    #region Private Fields

    /// <summary>The Windows notification icon stores 255 UTF-16 code units and a terminating null.</summary>
    public const int MaxCodeUnits = 255;

    #endregion

    #region Public Methods

    /// <summary>
    /// Drops control and bidirectional override characters, collapses whitespace, and cuts on a text element
    /// boundary so the result fits <see cref="MaxCodeUnits"/>.
    /// </summary>
    public static string Sanitize(string value)
    {
        var buffer = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var character in value)
        {
            if (IsBidirectionalOverride(character))
            {
                continue;
            }

            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingSpace = buffer.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                buffer.Append(' ');
                pendingSpace = false;
            }

            buffer.Append(character);
        }

        return Cut(buffer.ToString(), MaxCodeUnits);
    }

    /// <summary>True when a notification server said it will read the body as markup.</summary>
    public static bool UsesBodyMarkup(IReadOnlyList<string> capabilities) =>
        capabilities.Contains("body-markup");

    /// <summary>Escapes the five characters markup would treat as syntax. Plain text is left as it is.</summary>
    public static string EscapeIfMarkup(string value, bool bodyMarkup) =>
        bodyMarkup ? EscapeMarkup(value) : value;

    /// <summary>Escapes <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, quotes, and apostrophes for a markup body.</summary>
    public static string EscapeMarkup(string value)
    {
        var buffer = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            switch (character)
            {
                case '&':
                    buffer.Append("&amp;");
                    break;
                case '<':
                    buffer.Append("&lt;");
                    break;
                case '>':
                    buffer.Append("&gt;");
                    break;
                case '"':
                    buffer.Append("&quot;");
                    break;
                case '\'':
                    buffer.Append("&apos;");
                    break;
                default:
                    buffer.Append(character);
                    break;
            }
        }

        return buffer.ToString();
    }

    /// <summary>
    /// The longest prefix of <paramref name="value"/> that fits <paramref name="mostUnits"/> UTF-16 code units
    /// and ends on a text element.
    /// </summary>
    public static string Cut(string value, int mostUnits)
    {
        if (mostUnits < 1 || value.Length == 0)
        {
            return string.Empty;
        }

        if (value.Length <= mostUnits)
        {
            return value;
        }

        var info = new StringInfo(value);
        var count = info.LengthInTextElements;
        var taken = 0;
        var units = 0;

        for (var index = 0; index < count; index++)
        {
            var element = info.SubstringByTextElements(index, 1);
            if (units + element.Length > mostUnits)
            {
                break;
            }

            units += element.Length;
            taken++;
        }

        return taken == 0 ? string.Empty : info.SubstringByTextElements(0, taken);
    }

    #endregion

    #region Private Methods

    private static bool IsBidirectionalOverride(char character) => character is
        '\u202A' or '\u202B' or '\u202C' or '\u202D' or '\u202E' or
        '\u2066' or '\u2067' or '\u2068' or '\u2069';

    #endregion
}
