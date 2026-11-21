namespace Drover.Ui.Chat;

/// <summary>How a query is compared: by default like browser find, with two options a person can turn on.</summary>
public sealed record FindOptions(bool MatchCase = false, bool WholeWords = false);

/// <summary>Where one match sits inside one searched piece of a message.</summary>
public sealed record FindMatch(string MessageKey, FindPart Part, int Start, int Length);

/// <summary>Where a budgeted search stopped, so the next slice can continue from there.</summary>
public sealed record FindCursor(int SegmentIndex, int Offset);

/// <summary>One slice of a search: its matches, whether it reached the end, and where to resume.</summary>
public sealed record FindResult(IReadOnlyList<FindMatch> Matches, bool Finished, FindCursor Next);

/// <summary>
/// Matches text with a counted amount of work. The characters examined are reported after each call, so tests hold
/// the cost to a fixed multiple of the text instead of timing a machine. A caller can run a long conversation in
/// slices and resume from the returned cursor. Comparison is ordinal because the app runs with invariant
/// globalization; a whole word is bounded by letters, digits, marks and the underscore in an identifier.
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

    public FindResult Find(
        string query,
        IEnumerable<FindSegment> segments,
        FindOptions? options = null,
        int budget = DefaultBudget,
        FindCursor? start = null)
    {
        CharactersExamined = 0;
        List<FindMatch> matches = [];
        var all = segments is IReadOnlyList<FindSegment> listed ? listed : [.. segments];

        if (string.IsNullOrEmpty(query))
        {
            return new(matches, true, new(all.Count, 0));
        }

        // A window shorter than the query cannot hold a match and would leave the resume cursor where it started.
        var allowance = Math.Max(budget, query.Length);
        var effective = options ?? new();
        var comparison = effective.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var index = start?.SegmentIndex ?? 0;
        var offset = start?.Offset ?? 0;

        while (index < all.Count)
        {
            var text = all[index].Text;

            if (offset >= text.Length)
            {
                index++;
                offset = 0;
                continue;
            }

            var remaining = allowance - CharactersExamined;
            var end = Math.Min(text.Length, offset + remaining);
            var slice = text[offset..end];
            var from = 0;

            while (from <= slice.Length - query.Length)
            {
                var at = slice.IndexOf(query, from, comparison);

                if (at < 0)
                {
                    CharactersExamined += slice.Length - from;
                    break;
                }

                var cost = at - from + query.Length;

                if (CharactersExamined + cost > allowance)
                {
                    return new(matches, false, new(index, offset + from));
                }

                CharactersExamined += cost;

                if (!effective.WholeWords || IsWholeWord(text, offset + at, query.Length))
                {
                    matches.Add(new(all[index].MessageKey, all[index].Part, offset + at, query.Length));
                }

                from = at + 1;

                if (CharactersExamined >= allowance)
                {
                    return new(matches, false, new(index, offset + from));
                }
            }

            if (end < text.Length)
            {
                // A query that began just before the cut can finish after it. Resume early enough to see it, but
                // always advance so a caller looping on the cursor cannot stall.
                var resume = Math.Max(offset + 1, end - (query.Length - 1));
                return new(matches, false, new(index, resume));
            }

            index++;
            offset = 0;
        }

        return new(matches, true, new(all.Count, 0));
    }

    #endregion

    #region Private Methods

    private static bool IsWholeWord(string text, int start, int length) =>
        (start == 0 || !IsWordCharacter(text, start - 1))
        && (start + length == text.Length || !IsWordCharacter(text, start + length));

    private static bool IsWordCharacter(string text, int index)
    {
        if (index < 0)
        {
            return false;
        }

        var start = char.IsLowSurrogate(text[index]) && index > 0 ? index - 1 : index;
        var status = Rune.DecodeFromUtf16(text.AsSpan(start), out var rune, out _);

        return status == OperationStatus.Done
            && (rune.Value == '_' || Rune.IsLetterOrDigit(rune)
                || Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark
                    or UnicodeCategory.EnclosingMark);
    }

    #endregion
}
