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

    // Thirteen to nineteen digits, alone or in groups parted by one space or hyphen. Luhn decides which are cards.
    private static readonly Regex cardShaped = new(@"\d([ \-]?\d){12,18}", Options, MatchTimeout);

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
            // The engine says which construct it met; a person is told that in plain words, not in the engine's.
            return (null, $"That pattern {(unsupported.Message.Contains("backreference", StringComparison.OrdinalIgnoreCase) ? "refers back to a group" : "looks ahead or looks behind")}. Rules run patterns in a way that cannot stall, and such a pattern cannot run that way. Write it without that.");
        }
        catch (ArgumentException unreadable)
        {
            return (null, $"That pattern cannot be read: {unreadable.Message}");
        }
    }

    /// <summary>
    /// Every place in the text where the expression matches, as start and length. A match that runs out of time
    /// gives the whole text, so the rule applies and, if it redacts, nothing of the text is let through.
    /// </summary>
    public static List<(int Start, int Length)> Find(Regex expression, string text)
    {
        try
        {
            List<(int, int)> found = [];

            foreach (var match in expression.EnumerateMatches(text))
            {
                if (match.Length > 0)
                {
                    found.Add((match.Index, match.Length));
                }
            }

            return found;
        }
        catch (RegexMatchTimeoutException ranOut)
        {
            return ranOut.Input.Length == 0 ? [] : [(0, ranOut.Input.Length)];
        }
    }

    public static List<(int Start, int Length)> Find(BuiltinPattern builtin, string text) => builtin switch
    {
        BuiltinPattern.EmailAddress => Find(email, text),
        BuiltinPattern.Key => Find(key, text),
        _ => [.. Find(cardShaped, text).Where(place => PassesLuhn(text.AsSpan(place.Start, place.Length)))],
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

    private static bool PassesLuhn(ReadOnlySpan<char> shaped)
    {
        var sum = 0;
        var digits = 0;

        for (var index = shaped.Length - 1; index >= 0; index--)
        {
            if (!char.IsAsciiDigit(shaped[index]))
            {
                continue;
            }

            var digit = shaped[index] - '0';

            if (digits % 2 == 1)
            {
                digit *= 2;
                digit = digit > 9 ? digit - 9 : digit;
            }

            sum += digit;
            digits++;
        }

        return digits is >= 13 and <= 19 && sum % 10 == 0;
    }

    #endregion
}
