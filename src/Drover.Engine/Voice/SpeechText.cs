namespace Drover.Engine.Voice;

/// <summary>
/// Cuts text to read aloud into pieces a speech model takes in one request. A piece ends at the end of a sentence
/// where it can, at a space where it cannot, and in the middle of a word only when one word is longer than a piece.
/// </summary>
public static partial class SpeechText
{
    #region Private Fields

    /// <summary>What most speech endpoints take in one request.</summary>
    public const int DefaultPieceCharacters = 4000;

    /// <summary>One provider's speech model takes 200 characters a request, and answers only in WAV.</summary>
    public const int ShortPieceCharacters = 200;

    public const string ShortPieceHost = "api.groq.com";

    #endregion

    #region Public Methods

    /// <summary>How many characters one request to the speech service at <paramref name="address"/> may carry.</summary>
    public static int PieceCharactersFor(Uri address) => TakesShortPieces(address) ? ShortPieceCharacters : DefaultPieceCharacters;

    public static bool TakesShortPieces(Uri address) =>
        string.Equals(address.IdnHost, ShortPieceHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A reply as it should be heard: the marks that format it on a screen are taken out, a link is read by its
    /// words, a bare address is not read out letter by letter, and a block of code is named instead of recited.
    /// </summary>
    public static string Plain(string markdown)
    {
        var text = CodeBlock().Replace(markdown, "\n(a block of code)\n");
        text = Picture().Replace(text, "$1");
        text = Link().Replace(text, "$1");
        text = BareAddress().Replace(text, "(a link)");
        text = LineMark().Replace(text, string.Empty);
        text = TableRule().Replace(text, string.Empty);
        text = text.Replace('|', ',');
        text = Emphasis().Replace(text, "$2");
        text = text.Replace("`", string.Empty, StringComparison.Ordinal);
        return BlankLines().Replace(text, "\n\n").Trim();
    }

    /// <summary>The pieces of <paramref name="text"/>, in order, each at most <paramref name="maxCharacters"/> long and none empty.</summary>
    public static IReadOnlyList<string> Split(string text, int maxCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 2);
        List<string> pieces = [];
        var rest = text.AsSpan().Trim();

        while (rest.Length > maxCharacters)
        {
            var cut = FindCut(rest, maxCharacters);
            Add(pieces, rest[..cut]);
            rest = rest[cut..].TrimStart();
        }

        Add(pieces, rest);
        return pieces;
    }

    #endregion

    #region Private Methods

    private static void Add(List<string> pieces, ReadOnlySpan<char> piece)
    {
        var trimmed = piece.Trim();

        if (trimmed.Length > 0)
        {
            pieces.Add(trimmed.ToString());
        }
    }

    /// <summary>Where the first piece of <paramref name="text"/> ends: after a sentence, else after a space, else at the limit.</summary>
    private static int FindCut(ReadOnlySpan<char> text, int maxCharacters)
    {
        var window = text[..maxCharacters];
        var sentence = LastSentenceEnd(window);

        if (sentence > 0)
        {
            return sentence;
        }

        var space = window.LastIndexOfAny(' ', '\t');

        if (space > 0)
        {
            return space;
        }

        // Never between the two halves of one character.
        return char.IsHighSurrogate(window[^1]) ? maxCharacters - 1 : maxCharacters;
    }

    /// <summary>The index just past the last sentence end in <paramref name="window"/>, or zero when it holds none.</summary>
    private static int LastSentenceEnd(ReadOnlySpan<char> window)
    {
        for (var index = window.Length - 1; index > 0; index--)
        {
            var character = window[index];

            if (character is '\n' or '。' or '！' or '？')
            {
                return index + 1;
            }

            // A full stop inside a number or a name ("3.5", "example.org") does not end a sentence: a space must follow.
            if (character is '.' or '!' or '?' && (index == window.Length - 1 || char.IsWhiteSpace(window[index + 1])))
            {
                return index + 1;
            }
        }

        return 0;
    }

    #endregion

    #region Patterns

    [GeneratedRegex(@"^[ \t]*(```|~~~).*?^[ \t]*\1[ \t]*$", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CodeBlock();

    [GeneratedRegex(@"!\[([^\]\n]*)\]\([^)\n]*\)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Picture();

    [GeneratedRegex(@"\[([^\]\n]+)\]\([^)\n]*\)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Link();

    [GeneratedRegex(@"https?://[^\s)>\]]+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BareAddress();

    [GeneratedRegex(@"^[ \t]*(#{1,6}[ \t]+|>[ \t]?|[-*+][ \t]+(\[[ xX]\][ \t]+)?)", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex LineMark();

    [GeneratedRegex(@"^[ \t]*\|?[ \t]*:?-{3,}[-:| \t]*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex TableRule();

    [GeneratedRegex(@"(\*\*\*|\*\*|\*|___|__|~~)(?=\S)(.+?)(?<=\S)\1", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex BlankLines();

    #endregion
}
