namespace Drover.Domain.Rules;

/// <summary>
/// The patterns rules look for. Every expression runs on the engine that cannot backtrack, so the time a match
/// takes grows with the length of the text and never with the shape of the pattern, and under a time limit as well.
/// </summary>
public static partial class RulePatterns
{
    #region Private Fields

    public const int MaxPatternLength = 500;

    /// <summary>How long one match may take. A match that runs out of time counts as found: a rule fails closed.</summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private const RegexOptions Options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;

    private const int MaxCached = 512;

    private static readonly ConcurrentDictionary<string, Regex> written = new(StringComparer.Ordinal);

    private static readonly Regex email = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+", Options, MatchTimeout);

    private const int MinCardDigits = 13;

    private const int MaxCardDigits = 19;

    private const int MaxCardSeparator = 3;

    private const int MaxCardGroups = 6;

    private const int MinGroupDigits = 2;

    private const int MaxGroupDigits = 7;

    // Keys as the common services issue them, a private key block, and a bearer value.
    private static readonly Regex key = new(
        @"(sk|pk|rk)[\-_][A-Za-z0-9\-_]{16,}|gh[pousr]_[A-Za-z0-9]{20,}|xox[abprs]\-[A-Za-z0-9\-]{10,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z\-_]{30,}|\-\-\-\-\-BEGIN [A-Z ]*PRIVATE KEY\-\-\-\-\-|[Bb]earer [A-Za-z0-9\-_\.=]{20,}",
        Options,
        MatchTimeout);

    #endregion

    #region Public Methods

    /// <summary>
    /// The expression for a pattern a person wrote, or the reason it cannot be used, in words. Constructs that need
    /// backtracking (lookarounds, backreferences) are refused here, when the rule is saved.
    /// </summary>
    public static (Regex? Expression, string? Problem) Compile(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern.Length > MaxPatternLength)
        {
            return (null, $"Write a pattern of up to {MaxPatternLength} characters.");
        }

        if (written.TryGetValue(pattern, out var known))
        {
            return (known, null);
        }

        try
        {
            var compiled = new Regex(pattern, Options, MatchTimeout);

            if (written.Count < MaxCached)
            {
                written.TryAdd(pattern, compiled);
            }

            return (compiled, null);
        }
        catch (NotSupportedException unsupported)
        {
            return (null, $"That pattern looks ahead, looks behind, or refers back to a group. Rules run patterns in a way that cannot stall, and those cannot run that way. Write it without them. ({unsupported.Message})");
        }
        catch (ArgumentException unreadable)
        {
            return (null, $"That pattern cannot be read: {unreadable.Message}");
        }
    }

    /// <summary>
    /// Every place in the text where the expression matches, as start and length. A search that runs out of time,
    /// in one match or over all of them, gives the whole text: the rule applies and, if it redacts, nothing of the
    /// text is let through.
    /// </summary>
    public static List<(int Start, int Length)> Find(Regex expression, string text, RuleBudget budget)
    {
        if (text.Length == 0)
        {
            return [];
        }

        using var searching = budget.Search();

        try
        {
            List<(int, int)> found = [];

            for (var match = expression.Match(text); match.Success; match = match.NextMatch())
            {
                if (budget.IsSpent)
                {
                    return [(0, text.Length)];
                }

                if (match.Length > 0)
                {
                    found.Add((match.Index, match.Length));
                }
            }

            return budget.IsSpent ? [(0, text.Length)] : found;
        }
        catch (RegexMatchTimeoutException ranOut)
        {
            return [(0, ranOut.Input.Length)];
        }
    }

    public static List<(int Start, int Length)> Find(BuiltinPattern builtin, string text, RuleBudget budget) => builtin switch
    {
        BuiltinPattern.EmailAddress => Find(email, text, budget),
        BuiltinPattern.Key => Find(key, text, budget),
        _ => FindCards(text),
    };

    /// <summary>What a built-in pattern finds, in words for a sentence: "a card number".</summary>
    public static string Words(BuiltinPattern builtin) => builtin switch
    {
        BuiltinPattern.EmailAddress => "an email address",
        BuiltinPattern.CardNumber => "a card number",
        _ => "a key or token",
    };

    #endregion

    #region Private Methods

    // A card number is thirteen to nineteen digits, written in one piece or in groups parted by a little white
    // space, hyphens, or dots. Whole groups are tried from every group on, so a number, an expiry date, or a
    // security code standing next to the card does not hide it. Luhn decides which runs of groups are cards.
    private static List<(int Start, int Length)> FindCards(string text)
    {
        List<(int Start, int Length)> found = [];
        List<(int Start, int End)> groups = [];

        for (var index = 0; index <= text.Length;)
        {
            if (index < text.Length && char.IsDigit(text[index]))
            {
                var start = index;

                while (index < text.Length && char.IsDigit(text[index]))
                {
                    index++;
                }

                groups.Add((start, index));
                continue;
            }

            // The run of groups goes on over a short separator and ends at anything else.
            var gap = index;

            while (gap < text.Length && gap - index < MaxCardSeparator && IsCardSeparator(text[gap]))
            {
                gap++;
            }

            if (gap > index && gap < text.Length && char.IsDigit(text[gap]) && groups.Count > 0)
            {
                index = gap;
                continue;
            }

            AddCards(text, groups, found);
            groups.Clear();
            index = Math.Max(gap, index + 1);
        }

        return found;
    }

    private static void AddCards(string text, List<(int Start, int End)> groups, List<(int Start, int Length)> found)
    {
        for (var first = 0; first < groups.Count; first++)
        {
            var digits = 0;

            for (var last = first; last < groups.Count && digits < MaxCardDigits; last++)
            {
                digits += groups[last].End - groups[last].Start;

                // Written in groups, a card has a few groups of a few digits each. A column of small numbers is not one.
                var grouped = last > first;

                if (grouped && (last - first >= MaxCardGroups || groups.Skip(first).Take(last - first + 1).Any(group => group.End - group.Start is < MinGroupDigits or > MaxGroupDigits)))
                {
                    break;
                }

                if (digits is >= MinCardDigits and <= MaxCardDigits && PassesLuhn(text.AsSpan(groups[first].Start, groups[last].End - groups[first].Start)))
                {
                    found.Add((groups[first].Start, groups[last].End - groups[first].Start));
                    first = last;
                    break;
                }
            }
        }
    }

    private static bool IsCardSeparator(char character) =>
        character is '.' || char.IsWhiteSpace(character) || char.GetUnicodeCategory(character) is UnicodeCategory.DashPunctuation;

    private static bool PassesLuhn(ReadOnlySpan<char> shaped)
    {
        var sum = 0;
        var digits = 0;

        for (var index = shaped.Length - 1; index >= 0; index--)
        {
            if (!char.IsDigit(shaped[index]))
            {
                continue;
            }

            var digit = (int)char.GetNumericValue(shaped[index]);

            if (digits % 2 == 1)
            {
                digit *= 2;
                digit = digit > 9 ? digit - 9 : digit;
            }

            sum += digit;
            digits++;
        }

        return sum % 10 == 0;
    }

    #endregion
}
