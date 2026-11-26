namespace Drover.Engine.Voice;

/// <summary>
/// Cuts text to read aloud into pieces a speech model takes in one request. A piece ends at the end of a sentence
/// where it can, at a space where it cannot, and in the middle of a word only when one word is longer than a piece.
/// </summary>
public static class SpeechText
{
    #region Constants

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
}
