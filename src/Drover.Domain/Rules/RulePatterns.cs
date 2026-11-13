namespace Drover.Domain.Rules;

/// <summary>
/// The patterns rules look for. Every expression runs on the engine that cannot backtrack, so the time one match
/// takes grows with the length of the text and never with the shape of the pattern. No match has a clock of its
/// own. What can grow without bound is the number of searches and of matches, and the evaluation's one budget
/// bounds all of them together. An expression is built and warmed when it is made, outside any budget.
/// </summary>
public static partial class RulePatterns
{
    #region Private Fields

    public const int MaxPatternLength = 500;

    private const RegexOptions Options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;

    private const int MaxCached = 512;

    private static readonly ConcurrentDictionary<string, Regex> written = new(StringComparer.Ordinal);

    private static readonly Regex email = Warmed(new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+", Options));

    private const string WarmUp = "Warm-up text 0123 sam@example.org -_.";

    private const int MinCardDigits = 13;

    private const int MaxCardDigits = 19;

    private const int MaxCardSeparator = 4;

    // Keys as the common services issue them, a private key block, and a bearer value.
    private static readonly Regex key = Warmed(new(
        @"(sk|pk|rk)[\-_][A-Za-z0-9\-_]{16,}|gh[pousr]_[A-Za-z0-9]{20,}|xox[abprs]\-[A-Za-z0-9\-]{10,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z\-_]{30,}|\-\-\-\-\-BEGIN [A-Z ]*PRIVATE KEY\-\-\-\-\-|[Bb]earer [A-Za-z0-9\-_\.=]{20,}",
        Options));

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
            var compiled = Warmed(new Regex(pattern, Options));

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
    /// Every place in the text where the expression matches, as start and length. A search that runs out of the
    /// evaluation's budget gives the whole text: the rule applies and, if it redacts, nothing of the text is let
    /// through. So does an expression that was given a time limit of its own and ran out of it.
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
            // The whole text, by its own length: what the exception carries of the input can be empty.
            return [(0, Math.Max(text.Length, ranOut.Input.Length))];
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

    // A card number is thirteen to nineteen digits, written in one piece or in groups with a few characters that
    // are neither letters nor digits between them. Every run of whole groups that adds up to a card's length is
    // tried, from every group on, and every run that passes Luhn is a place: so a number, a date, or a security
    // code standing next to the card neither hides it nor is left behind with a part of it.
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

            // The run of groups goes on over a short separator and ends at a letter or a longer gap.
            var gap = index;

            while (gap < text.Length && gap - index < MaxCardSeparator && !char.IsLetterOrDigit(text[gap]))
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

                if (digits is >= MinCardDigits and <= MaxCardDigits && PassesLuhn(text.AsSpan(groups[first].Start, groups[last].End - groups[first].Start)))
                {
                    found.Add((groups[first].Start, groups[last].End - groups[first].Start));
                }
            }
        }
    }

    // An expression builds its matcher the first time it is used. That happens here, when the expression is made
    // (the built-in ones when the server starts, a written one when its rule is saved or first read), so the
    // first search of a run never pays for it out of the evaluation's budget.
    private static Regex Warmed(Regex expression)
    {
        expression.IsMatch(WarmUp);
        return expression;
    }

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
