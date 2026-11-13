namespace Drover.Domain.Rules;

/// <summary>
/// A piece of text as rules compare it: without characters that cannot be seen, and with full-width letters, digits
/// of any script, and unusual spaces in their plain form, so a zero-width space inside a word does not hide the
/// word. A letter of another alphabet that only looks the same is not made the same. Each character of the plain form remembers where it came from, so what matched can be removed
/// from the text as it was written.
/// </summary>
public sealed class RuleText
{
    #region Private Fields

    private const char FullWidthFirst = '\uFF01';

    private const char FullWidthLast = '\uFF5E';

    private const int FullWidthOffset = 0xFEE0;

    private readonly int[] starts;

    private readonly int[] ends;

    #endregion

    #region Constructors

    public RuleText(string original)
    {
        Original = original;

        if (IsPlain(original))
        {
            Plain = original;
            (starts, ends) = ([], []);
            return;
        }

        var plain = new StringBuilder(original.Length);
        List<int> from = [];
        List<int> to = [];

        for (var index = 0; index < original.Length;)
        {
            var length = char.IsHighSurrogate(original[index]) && index + 1 < original.Length && char.IsLowSurrogate(original[index + 1]) ? 2 : 1;
            var piece = original.Substring(index, length);

            if (!IsUnseen(piece))
            {
                foreach (var character in Normal(piece))
                {
                    plain.Append(character);
                    from.Add(index);
                    to.Add(index + length);
                }
            }

            index += length;
        }

        Plain = plain.ToString();
        (starts, ends) = ([.. from], [.. to]);
    }

    #endregion

    #region Properties

    public string Original { get; }

    /// <summary>The text as it is compared.</summary>
    public string Plain { get; }

    /// <summary>Whether the text is its own plain form: nothing unseen in it, and no character in another form.</summary>
    public bool IsAsWritten => ReferenceEquals(Plain, Original) || string.Equals(Plain, Original, StringComparison.Ordinal);

    #endregion

    #region Public Methods

    /// <summary>Where a stretch of the plain form lies in the text as written.</summary>
    public (int Start, int Length) InOriginal(int start, int length)
    {
        if (starts.Length == 0 || length == 0)
        {
            return (start, length);
        }

        var first = starts[start];
        return (first, ends[start + length - 1] - first);
    }

    #endregion

    #region Private Methods

    // Most text is plain already: printable ASCII has no other form and nothing in it is unseen.
    private static bool IsPlain(string text)
    {
        foreach (var character in text)
        {
            if (character > '\u007E' || (character < ' ' && character is not ('\n' or '\r' or '\t')))
            {
                return false;
            }
        }

        return true;
    }

    // What cannot be seen: format characters (zero-width spaces and joiners, direction marks, the soft hyphen),
    // marks that attach to the character before them (combining accents, variation selectors, the grapheme
    // joiner), and controls other than line breaks and tabs.
    private static bool IsUnseen(string piece) =>
        CharUnicodeInfo.GetUnicodeCategory(piece, 0) is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark
        || (piece.Length == 1 && char.IsControl(piece[0]) && piece[0] is not ('\n' or '\r' or '\t'));

    // The plain form of one character, worked out here because the server runs without the system's Unicode
    // tables: a full-width letter, digit, or sign becomes the ordinary one, a digit of any script becomes 0 to 9,
    // and any space becomes a space. Letters of other alphabets that only look like these stay what they are.
    private static string Normal(string piece)
    {
        if (piece.Length == 2)
        {
            var rune = Rune.GetRuneAt(piece, 0);
            return Rune.IsDigit(rune) ? ((char)('0' + (int)Rune.GetNumericValue(rune))).ToString() : piece;
        }

        var character = piece[0];

        if (character is >= FullWidthFirst and <= FullWidthLast)
        {
            return ((char)(character - FullWidthOffset)).ToString();
        }

        if (char.IsDigit(character))
        {
            return ((char)('0' + (int)char.GetNumericValue(character))).ToString();
        }

        return char.IsWhiteSpace(character) && character is not ('\n' or '\r' or '\t') ? " " : piece;
    }

    #endregion
}
