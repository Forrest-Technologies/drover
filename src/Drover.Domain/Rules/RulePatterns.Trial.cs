namespace Drover.Domain.Rules;

/// <summary>What can be read off a written pattern when its rule is saved: its counts, and whether the engine can build it.</summary>
public static partial class RulePatterns
{
    #region Private Fields

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

    #endregion
}
