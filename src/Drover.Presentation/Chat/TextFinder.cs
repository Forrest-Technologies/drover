namespace Drover.Ui.Chat;

/// <summary>How a query is compared: by default like browser find, with two options a person can turn on.</summary>
public sealed record FindOptions(bool MatchCase = false, bool WholeWords = false);

/// <summary>Where one match sits inside one searched piece of a message.</summary>
public sealed record FindMatch(string MessageKey, FindPart Part, int Start, int Length);

/// <summary>
/// Matches text with a counted amount of work. The characters examined are reported after each call, so tests hold
/// the cost to a fixed multiple of the text instead of timing a machine. Comparison is ordinal because the app runs
/// with invariant globalization; a whole word is bounded by letters, digits and the underscore in an identifier.
/// </summary>
public sealed class TextFinder
{
    #region Private Fields

    private const int DefaultBudget = 1_000_000;

    #endregion

    #region Properties

    public int CharactersExamined { get; private set; }

    #endregion

    #region Public Methods

    public IReadOnlyList<FindMatch> Find(
        string query,
        IEnumerable<FindSegment> segments,
        FindOptions? options = null,
        int budget = DefaultBudget)
    {
        CharactersExamined = 0;
        List<FindMatch> matches = [];

        if (string.IsNullOrEmpty(query))
        {
            return matches;
        }

        var effective = options ?? new();
        var comparison = effective.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var segment in segments)
        {
            if (CharactersExamined >= budget)
            {
                break;
            }

            var text = segment.Text;
            var limit = Math.Min(text.Length, budget - CharactersExamined);

            if (limit <= 0)
            {
                break;
            }

            text = text[..limit];
            CharactersExamined += limit;
            var from = 0;

            while (from <= text.Length - query.Length)
            {
                var at = text.IndexOf(query, from, comparison);

                if (at < 0)
                {
                    break;
                }

                CharactersExamined += query.Length;

                if (!effective.WholeWords || IsWholeWord(text, at, query.Length))
                {
                    matches.Add(new(segment.MessageKey, segment.Part, at, query.Length));
                }

                from = at + 1;

                if (CharactersExamined >= budget)
                {
                    break;
                }
            }
        }

        return matches;
    }

    #endregion

    #region Private Methods

    private static bool IsWholeWord(string text, int start, int length) =>
        (start == 0 || !IsWordCharacter(text[start - 1]))
        && (start + length == text.Length || !IsWordCharacter(text[start + length]));

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    #endregion
}
