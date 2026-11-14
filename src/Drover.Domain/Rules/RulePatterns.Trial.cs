namespace Drover.Domain.Rules;

/// <summary>
/// Trying a written pattern before it is trusted with real text: its counts, whether the engine can build it, and
/// what it costs on sample text made to bring out the states a costly pattern has.
/// </summary>
public static partial class RulePatterns
{
    #region Private Fields

    private const string TooMuchWork =
        "That pattern takes too much work to run. It was tried on sample text and went over what one pattern may use. The usual cause is a wide gap, such as .* or .{0,200}, in front of something specific. Make the gap smaller, or say more exactly what comes before it.";

    private const int TrialLength = 1500;

    private const int TrialStep = 32;

    private const int MostPairs = 8;

    private static readonly SearchValues<char> countCharacters = SearchValues.Create("0123456789,");

    // Characters of every sort a pattern's classes are likely to tell apart.
    private const string Ordinary = "aZ k7_.-@\n:/=\"";

    #endregion

    #region Private Methods

    // The expression, or why the engine will not have the pattern, in a person's words and not in the engine's.
    private static (Regex? Expression, string? Problem) Build(string pattern)
    {
        if (FindRepeatProblem(pattern) is { } repeats)
        {
            return (null, repeats);
        }

        try
        {
            return (new Regex(pattern, Options, matchTimeout), null);
        }
        catch (NotSupportedException unsupported)
        {
            // The engine says which construct it met; a person is told that in plain words, not in the engine's.
            return (null, $"That pattern {Reason(unsupported.Message)}. Rules run patterns in a way that cannot stall, and such a pattern cannot run that way. Write it without that.");
        }
        catch (ArgumentException unreadable)
        {
            return (null, $"That pattern cannot be read: {unreadable.Message}");
        }
    }

    private static string Reason(string enginesWords) =>
        enginesWords.Contains("backreference", StringComparison.OrdinalIgnoreCase) ? "refers back to a group"
        : enginesWords.Contains("automata", StringComparison.OrdinalIgnoreCase) ? "is too large once its counts are written out"
        : "looks ahead or looks behind";

    // A count such as {300} or {2,300}, outside a class and not escaped, that is over the most a pattern may repeat.
    private static string? FindRepeatProblem(string pattern)
    {
        var inClass = false;

        for (var index = 0; index < pattern.Length; index++)
        {
            switch (pattern[index])
            {
                case '\\':
                    index++;
                    break;
                case '[' when !inClass:
                    inClass = true;
                    break;
                case ']' when inClass:
                    inClass = false;
                    break;
                case '{' when !inClass && LargestCount(pattern.AsSpan(index + 1)) > MaxRepeat:
                    return $"That pattern repeats something more than {MaxRepeat} times. Use a smaller count.";
                default:
                    break;
            }
        }

        return null;
    }

    // The larger number of a count that starts here ("300}", "2,300}", "2,}"), or nothing when this is no count.
    private static int LargestCount(ReadOnlySpan<char> after)
    {
        var end = after.IndexOf('}');

        if (end <= 0 || after[..end].ContainsAnyExcept(countCharacters) || after[..end].Count(',') > 1)
        {
            return 0;
        }

        var largest = 0;

        foreach (var part in after[..end].Split(','))
        {
            var digits = after[..end][part];
            largest = Math.Max(largest, digits.Length == 0 ? 0 : int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : int.MaxValue);
        }

        return largest;
    }

    // Runs the expression over the sample texts a stretch at a time and looks at what it has cost after each. The
    // engine cannot be stopped inside a stretch, so a stretch is short: what one can cost is the most this goes
    // over its ceiling. States the expression has built stay built, so reading a text again from its start is cheap.
    private static string? FindTrialProblem(Regex expression, string pattern, long allocatedBefore)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            foreach (var text in TrialTexts(pattern))
            {
                for (var end = TrialStep; end <= text.Length; end += TrialStep)
                {
                    foreach (var match in expression.EnumerateMatches(text.AsSpan(0, end)))
                    {
                        _ = match;
                    }

                    if (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore > MaxTrialBytes || watch.Elapsed > maxTrialTime)
                    {
                        return TooMuchWork;
                    }
                }
            }

            return null;
        }
        catch (RegexMatchTimeoutException ranOut)
        {
            return $"{TooMuchWork} (One match ran longer than {ranOut.MatchTimeout.TotalSeconds:0} second.)";
        }
    }

    // Sample texts for one pattern, the same every time. A costly pattern is costly on text made of the characters
    // it names: all of them, each of them with one other character, and the words it spells out with a little
    // between them (what it names, letters only, digits only). Ordinary characters are mixed in so its classes have something to tell apart.
    private static List<string> TrialTexts(string pattern)
    {
        var named = new string([.. pattern.Where(character => char.IsLetterOrDigit(character) || Ordinary.Contains(character, StringComparison.Ordinal)).Distinct()]);
        named = named.Length == 0 ? "a" : named;
        List<string> texts =
        [
            Scatter(named, TrialLength, 3),
            Scatter($"{named}{Ordinary}", TrialLength, 5),
            Spell(pattern, $"{named}{Ordinary}"),
            Spell(pattern, "abc"),
            Spell(pattern, "012"),
        ];

        foreach (var one in named.Take(MostPairs))
        {
            texts.Add(Scatter($"{one}{(one is 'a' ? 'b' : 'a')}", TrialLength, 7 + one));
        }

        return texts;
    }

    private static string Scatter(string alphabet, int length, int seed)
    {
        var random = new Random(seed);
        return string.Create(length, (alphabet, random), static (span, state) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = state.alphabet[state.random.Next(state.alphabet.Length)];
            }
        });
    }

    // The runs of letters and digits the pattern spells, also where it spells them one class or group at a time,
    // each followed by a few characters to fill a gap. A gap that takes anything on a line is filled by letters or
    // by digits; one that is ended by what the pattern names needs the filling not to hold it.
    private static string Spell(string pattern, string filling)
    {
        var skeleton = new string([.. pattern.Where(character => character is not ('[' or ']' or '(' or ')'))]);
        List<string> words = [.. spelled.Matches(skeleton).Select(match => match.Value).Distinct(StringComparer.Ordinal)];
        var random = new Random(11);
        var text = new StringBuilder(TrialLength + MaxPatternLength);

        while (text.Length < TrialLength)
        {
            text.Append(words.Count > 0 ? words[random.Next(words.Count)] : "a");
            text.Append(Scatter(filling, random.Next(0, 6), random.Next()));
        }

        return text.ToString(0, TrialLength);
    }

    [GeneratedRegex("[A-Za-z0-9]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex SpelledWords();

    private static readonly Regex spelled = SpelledWords();

    #endregion
}
