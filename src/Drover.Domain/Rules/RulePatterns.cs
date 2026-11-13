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

    private const int GroupOfFour = 4;

    private const int MinCardDigits = 13;

    private const int MaxCardDigits = 19;

    // The ways a card is printed in groups after its first four digits: 4-4-4-4-3, 4-4-4-4, and 4-6-5.
    private static readonly int[][] cardGroups = [[4, 4, 4, 3], [4, 4, 4], [6, 5]];

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
            // The engine says which construct it met; a person is told that in plain words, not in the engine's.
            return (null, $"That pattern {(unsupported.Message.Contains("backreference", StringComparison.OrdinalIgnoreCase) ? "refers back to a group" : "looks ahead or looks behind")}. Rules run patterns in a way that cannot stall, and such a pattern cannot run that way. Write it without that.");
        }
        catch (ArgumentException unreadable)
        {
            return (null, $"That pattern cannot be read: {unreadable.Message}");
        }
    }

    /// <summary>
    /// Every place in the text where the expression matches, as start and length, or null when the budget could not
    /// pay for the search: one reading of the text, and each match by how far it moved on. An expression that was
    /// given a time limit of its own and ran out of it could not be searched either.
    /// </summary>
    public static List<(int Start, int Length)>? Find(Regex expression, string text, RuleBudget budget)
    {
        if (!budget.TrySpend(text.Length))
        {
            return null;
        }

        try
        {
            List<(int, int)> found = [];
            var from = 0;

            for (var match = expression.Match(text); match.Success; match = match.NextMatch())
            {
                if (!budget.TrySpend(match.Index + match.Length - from + 1))
                {
                    return null;
                }

                from = match.Index + match.Length;

                if (match.Length > 0)
                {
                    found.Add((match.Index, match.Length));
                }
            }

            return found;
        }
        catch (RegexMatchTimeoutException ranOut)
        {
            // The text was not searched to its end, so it could not be checked. Only a limit that is not one could
            // mean otherwise, and there is none.
            return ranOut.MatchTimeout == Timeout.InfiniteTimeSpan ? [] : null;
        }
    }

    public static List<(int Start, int Length)>? Find(BuiltinPattern builtin, string text, RuleBudget budget) => builtin switch
    {
        BuiltinPattern.EmailAddress => Find(email, text, budget),
        BuiltinPattern.Key => Find(key, text, budget),
        _ => FindCards(text, budget),
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

    // A card number as cards are written: thirteen to nineteen digits in one piece, or in the groups a card is
    // printed in (four fours, with or without three more, or four, six, five) parted by one space or one hyphen.
    // Each group is a whole run of digits, so the number is bounded by something that is not a digit on both
    // sides. It has to begin the way a card network's numbers begin, have a length that network uses, and pass
    // Luhn. Other spellings (dots, double spaces, single digits) are not looked for: looking for them takes
    // dates, prices, and rows of numbers for cards.
    private static List<(int Start, int Length)>? FindCards(string text, RuleBudget budget)
    {
        // One pass over the text. A group of four looks a bounded way ahead, so the pass stays one reading.
        if (!budget.TrySpend(text.Length))
        {
            return null;
        }

        List<(int Start, int Length)> found = [];

        for (var index = 0; index < text.Length;)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                index++;
                continue;
            }

            var end = EndOfDigits(text, index);
            var grouped = end - index is GroupOfFour ? EndOfGroups(text, index, end) : -1;
            var last = grouped > 0 ? grouped : end;

            var isCard = grouped > 0
                ? StandsAlone(text, index, grouped)
                : end - index is >= MinCardDigits and <= MaxCardDigits && !IsPartOfADecimal(text, index, end) && IsCard(text.AsSpan(index, end - index));

            if (isCard)
            {
                found.Add((index, last - index));
            }

            // A group of four that began no card may still be followed by one.
            index = found.Count > 0 && found[^1].Start == index ? last : end;
        }

        return found;
    }

    // A card written in groups is not four numbers out of a row of numbers: the group before it and the group
    // after it, when one is joined on by a space or a hyphen, is short (a count, a month, a security code).
    private static bool StandsAlone(string text, int start, int end)
    {
        var before = start >= 2 && text[start - 1] is ' ' or '-' ? start - 1 : -1;
        var runBefore = before;

        while (runBefore > 0 && char.IsAsciiDigit(text[runBefore - 1]))
        {
            runBefore--;
        }

        var after = end < text.Length - 1 && text[end] is ' ' or '-' ? EndOfDigits(text, end + 1) - (end + 1) : 0;
        return (before < 0 || before - runBefore < GroupOfFour) && after < GroupOfFour;
    }

    // The digits after a decimal point, or before one, are a number and not a card.
    private static bool IsPartOfADecimal(string text, int start, int end) =>
        (start >= 2 && text[start - 1] is '.' && char.IsAsciiDigit(text[start - 2]))
        || (end < text.Length - 1 && text[end] is '.' && char.IsAsciiDigit(text[end + 1]));

    private static int EndOfDigits(string text, int start)
    {
        var end = start;

        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        return end;
    }

    // Where a card written in groups ends when its first group of four ends at firstEnd, or -1 when what follows is
    // not one of the ways a card is grouped. The longest way that is a card is taken.
    private static int EndOfGroups(string text, int start, int firstEnd)
    {
        if (firstEnd >= text.Length || text[firstEnd] is not (' ' or '-'))
        {
            return -1;
        }

        var best = -1;

        foreach (var shape in cardGroups)
        {
            var end = firstEnd;

            foreach (var size in shape)
            {
                // The same separator throughout, one character, and each group a whole run of digits.
                var next = end + 1;
                end = end < text.Length && text[end] == text[firstEnd] && EndOfDigits(text, next) - next == size ? next + size : -1;

                if (end < 0)
                {
                    break;
                }
            }

            if (end > best && IsCard(text.AsSpan(start, end - start)))
            {
                best = end;
            }
        }

        return best;
    }

    private static bool IsCard(ReadOnlySpan<char> written)
    {
        Span<char> digits = stackalloc char[MaxCardDigits];
        var count = 0;

        foreach (var character in written)
        {
            if (char.IsAsciiDigit(character) && count < MaxCardDigits)
            {
                digits[count++] = character;
            }
        }

        return IsIssued(digits[..count]) && PassesLuhn(digits[..count]);
    }

    // Whether a number begins the way a card network's numbers begin and has a length that network uses.
    private static bool IsIssued(ReadOnlySpan<char> digits)
    {
        var length = digits.Length;
        var two = ((digits[0] - '0') * 10) + (digits[1] - '0');
        var four = (two * 100) + ((digits[2] - '0') * 10) + (digits[3] - '0');
        return digits[0] switch
        {
            '4' => length is 13 or 16 or 19,
            '5' when two is >= 51 and <= 55 => length is 16,
            '5' => two is 50 or >= 56 && length is >= 13,
            '2' => four is >= 2221 and <= 2720 && length is 16,
            '3' when two is 34 or 37 => length is 15,
            '3' when two is 35 => length is >= 16,
            '3' => two is 30 or 36 or 38 or 39 && length is >= 14,
            '6' => length is >= 13,
            _ => false,
        };
    }

    // An expression builds its matcher the first time it is used. That happens here, when the expression is made
    // (the built-in ones when the server starts, a written one when its rule is saved or first read), so the
    // first search of a run never pays for it out of the evaluation's budget.
    private static Regex Warmed(Regex expression)
    {
        expression.IsMatch(WarmUp);
        return expression;
    }

    private static bool PassesLuhn(ReadOnlySpan<char> digits)
    {
        var sum = 0;

        for (var index = digits.Length - 1; index >= 0; index--)
        {
            var digit = digits[index] - '0';

            if ((digits.Length - 1 - index) % 2 == 1)
            {
                digit *= 2;
                digit = digit > 9 ? digit - 9 : digit;
            }

            sum += digit;
        }

        return sum % 10 == 0;
    }

    #endregion
}
