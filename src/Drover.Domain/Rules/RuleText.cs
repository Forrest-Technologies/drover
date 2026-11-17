namespace Drover.Domain.Rules;

/// <summary>
/// A piece of text as rules compare it: without characters that cannot be seen, and with full-width letters, digits
/// of any script, unusual spaces, and full stops of other scripts in their plain form, so a zero-width space inside
/// a word does not hide the word. A letter of another alphabet that only looks the same is not made the same. Each
/// character of the plain form remembers where it came from, so what matched can be removed from the text as it
/// was written.
/// </summary>
public sealed class RuleText
{
    #region Private Fields

    private const char FullWidthFirst = '！';

    private const char FullWidthLast = '～';

    private const int FullWidthOffset = 0xFEE0;

    private readonly int[] starts;

    private readonly int[] ends;

    private string? folded;

    #endregion

    #region Constructors

    private RuleText(string original)
    {
        Original = original;

        if (IsPlain(original))
        {
            Plain = original;
            (starts, ends) = ([], []);
            return;
        }

        var plain = new StringBuilder(original.Length);
        var from = new int[original.Length];
        var to = new int[original.Length];

        for (var index = 0; index < original.Length;)
        {
            var character = original[index];
            var paired = char.IsHighSurrogate(character) && index + 1 < original.Length && char.IsLowSurrogate(original[index + 1]);
            var length = paired ? 2 : 1;

            if (paired)
            {
                var rune = new Rune(character, original[index + 1]);

                if (Rune.IsDigit(rune))
                {
                    Keep(plain, from, to, (char)('0' + (int)Rune.GetNumericValue(rune)), index, length);
                }
                else if (!IsUnseen(Rune.GetUnicodeCategory(rune)))
                {
                    Keep(plain, from, to, character, index, length);
                    Keep(plain, from, to, original[index + 1], index, length);
                }
            }
            else if (!IsUnseen(char.GetUnicodeCategory(character)) && !(char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
            {
                Keep(plain, from, to, Normal(character), index, length);
            }

            index += length;
        }

        Plain = plain.ToString();
        (starts, ends) = (from, to);
    }

    #endregion

    #region Properties

    public string Original { get; }

    /// <summary>The text as it is compared.</summary>
    public string Plain { get; }

    /// <summary>
    /// The plain form with every letter in its capital, character for character, so that text is found without
    /// regard to case by comparing characters as they are. Worked out once.
    /// </summary>
    public string Folded => folded ??= Plain.ToUpperInvariant();

    /// <summary>Whether the text is its own plain form: nothing unseen in it, and no character in another form.</summary>
    public bool IsAsWritten => ReferenceEquals(Plain, Original) || string.Equals(Plain, Original, StringComparison.Ordinal);

    #endregion

    #region Public Methods

    /// <summary>
    /// The text in its plain form, paid for from the evaluation's budget, or null when the budget cannot pay for
    /// reading it.
    /// </summary>
    public static RuleText? Read(string original, RuleBudget budget) => budget.TrySpend(original.Length) ? new RuleText(original) : null;

    /// <summary>Where a stretch of the plain form lies in the text as written.</summary>
    public (int Start, int Length) InOriginal(int start, int length)
    {
        if (ReferenceEquals(Plain, Original) || length == 0)
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
            if (character > '~' || (character < ' ' && character is not ('\n' or '\r' or '\t')))
            {
                return false;
            }
        }

        return true;
    }

    // The plain form never has more characters than the text, so the place of each is kept by its index in it.
    private static void Keep(StringBuilder plain, int[] from, int[] to, char character, int index, int length)
    {
        from[plain.Length] = index;
        to[plain.Length] = index + length;
        plain.Append(character);
    }

    // What cannot be seen: format characters (zero-width spaces and joiners, direction marks, the soft hyphen) and
    // marks that attach to the character before them (combining accents, variation selectors, the grapheme joiner).
    private static bool IsUnseen(UnicodeCategory category) =>
        category is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark;

    // The plain form of one character, worked out here because the server runs without the system's Unicode
    // tables: a full-width letter, digit, or sign becomes the ordinary one, a digit of any script becomes 0 to 9,
    // any space becomes a space, and a full stop of another script becomes a full stop.
    private static char Normal(char character)
    {
        if (character is >= FullWidthFirst and <= FullWidthLast)
        {
            return (char)(character - FullWidthOffset);
        }

        if (char.IsDigit(character))
        {
            return (char)('0' + (int)char.GetNumericValue(character));
        }

        if (character is '。' or '｡')
        {
            return '.';
        }

        return char.IsWhiteSpace(character) && character is not ('\n' or '\r' or '\t') ? ' ' : character;
    }

    #endregion
}
