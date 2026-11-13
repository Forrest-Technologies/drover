namespace Drover.Domain.Rules;

/// <summary>
/// Decides which rule applies to a subject. This is the one place rules are evaluated: a run and the editor's
/// "Try it" both call it, so what a person is shown is what a run does. Same input, same outcome; no model is asked.
/// Conditions are of two kinds. An exact condition is decided on structure (which tool, its kind, the time, a count,
/// the values of named inputs) and is guaranteed. A pattern condition looks for something in text and catches the
/// common forms. Either kind applies when it could not be checked.
/// </summary>
public static class RuleEvaluator
{
    #region Private Fields

    public const string Redaction = "[removed by a rule]";

    private const string TooMuch = "there is too much to check";

    private const string OutOfWork = "it could not be checked within the work one evaluation may do";

    private const string NothingLetThrough = ", so nothing of it is let through";

    private static readonly JsonElement nothing = JsonElement.Parse("{}");

    #endregion

    #region Public Methods

    /// <summary>
    /// The outcome for a subject, or null when no rule applies. Of the rules that apply, the strictest effect decides
    /// (stop, then block, then ask, then redact), and among equals the first in order. Effects hold together: every
    /// applying rule that redacts is carried out whichever rule decides. A rule removes text from text; where what
    /// matched cannot be rewritten (the name of an input, a number, something there is too much of to read), the
    /// rule blocks. The whole evaluation spends from one budget of work and looks at no more than one cap of text.
    /// </summary>
    public static RuleOutcome? Evaluate(IReadOnlyList<ActiveRule> rules, RuleSubject subject) =>
        Evaluate(rules, subject, new RuleBudget(RuleBudget.MaxWork));

    public static RuleOutcome? Evaluate(IReadOnlyList<ActiveRule> rules, RuleSubject subject, RuleBudget budget)
    {
        var reading = new RuleReading(subject, budget);
        List<(ActiveRule Rule, string Why)> holding = [];

        foreach (var rule in rules)
        {
            if (rule.Checkpoint == subject.Checkpoint && Applies(rule, reading) is { } why)
            {
                holding.Add((rule, why));
            }
        }

        if (holding.Count == 0)
        {
            return null;
        }

        // The rewrite is made before the effects are settled, because a rule that removes text and cannot (for
        // what it matched, or for the work it takes) blocks instead, and that can change which rule decides.
        // Rules can be equal to one another (the same rule from two bots), so each keeps its place in the list.
        var redacting = holding.Select(entry => entry.Rule).Where(rule => rule.Effect is RuleEffect.Redact).ToList();
        List<string?> obstacles = [.. holding.Select(entry => entry.Rule.Effect is RuleEffect.Redact ? CannotRewrite(entry.Rule, reading) : null)];
        var rewritten = redacting.Count > 0 && obstacles.All(obstacle => obstacle is null) ? Rewrite(reading, redacting) : null;
        var rewriteFailed = redacting.Count > 0 && rewritten is null;
        List<(ActiveRule Rule, string Why, RuleEffect Effect)> applied =
        [
            .. holding.Select((entry, index) => entry.Rule.Effect is RuleEffect.Redact && rewriteFailed
                ? (entry.Rule, $"{entry.Why}{obstacles[index] ?? NothingLetThrough}", RuleEffect.Block)
                : (entry.Rule, entry.Why, entry.Rule.Effect)),
        ];
        var (deciding, reason, effect) = applied.MaxBy(entry => entry.Effect);
        var outcome = new RuleOutcome(deciding, effect, reason)
        {
            Applied = [deciding, .. applied.Select(entry => entry.Rule).Where(rule => rule != deciding)],
        };

        if (rewritten is { } clean)
        {
            return outcome with { RedactedText = clean.Text, RedactedJson = clean.Json };
        }

        // Nothing is kept of a subject a rule could not rewrite, nor of a call that is refused for what stands in
        // the name of one of its inputs: what is stored must not hold what matched.
        var keepsNothing = rewriteFailed || (effect >= RuleEffect.Block && holding.Any(entry => MatchesInAName(entry.Rule, reading)));
        return keepsNothing
            ? outcome with { RedactedText = subject.Text is null ? null : Redaction, RedactedJson = subject.Json is null ? null : nothing }
            : outcome;
    }

    #endregion

    #region Conditions

    // The reason the rule applies, in words that never hold the text that matched, or null when it does not apply.
    private static string? Applies(ActiveRule rule, RuleReading reading)
    {
        List<string> reasons = [];

        foreach (var condition in rule.Conditions)
        {
            if (Holds(condition, rule, reading) is not { } reason)
            {
                return null;
            }

            reasons.Add(reason);
        }

        return reasons.Count == 0 ? "it always applies" : string.Join(", and ", reasons);
    }

    private static string? Holds(RuleCondition condition, ActiveRule rule, RuleReading reading)
    {
        var subject = reading.Subject;
        return condition.Kind switch
        {
            RuleConditionKind.ToolIs => subject.ToolName is { } tool && condition.Tools.Contains(tool, StringComparer.OrdinalIgnoreCase)
                ? $"the tool is {tool}"
                : null,
            RuleConditionKind.ToolKind => subject.ToolKind is { } kind && condition.ToolKinds.Contains(kind)
                ? $"the tool {KindWords(kind)}"
                : null,
            RuleConditionKind.TextContains => Finds(condition, reading, $"{Where(condition)} contains text the rule names"),
            RuleConditionKind.TextMatches => Finds(condition, reading, $"{Where(condition)} matches the rule's pattern"),
            RuleConditionKind.TextHolds => Finds(condition, reading, $"{Where(condition)} holds {RulePatterns.Words(condition.Builtin ?? BuiltinPattern.Key)}"),
            RuleConditionKind.NotInList when condition.Argument is null => HasAnAddressOffTheList(condition, reading),
            RuleConditionKind.NotInList => IsOffTheList(condition, rule, reading),
            RuleConditionKind.TimeIs => IsAtThisTime(condition, subject.Now) ? "it is within the rule's hours" : null,
            RuleConditionKind.RunExceeds => Exceeds(condition, subject),
            _ => null,
        };
    }

    // A pattern condition: whether what it looks for is in any piece of text it looks at. What could not be read,
    // for its size or for the work it takes, counts as found.
    private static string? Finds(RuleCondition condition, RuleReading reading, string found)
    {
        if (reading.IsTooMuch)
        {
            return TooMuch;
        }

        var search = reading.Search(condition);
        return search.CouldNotCheck ? OutOfWork : search.Found ? found : null;
    }

    // An exact condition. The rule names inputs, matched without regard to case, and every value of every one of
    // them that is in the call has to be on the list, each time it occurs. When none of them is there and the rule
    // says which tools it is about, it applies, because the input should have been.
    private static string? IsOffTheList(RuleCondition condition, ActiveRule rule, RuleReading reading)
    {
        var names = RuleReading.Names(condition.Argument);
        var present = false;

        foreach (var property in reading.Subject.Json is { ValueKind: JsonValueKind.Object } json ? json.EnumerateObject() : default)
        {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            present = true;

            foreach (var value in Values(property.Value))
            {
                if (IsListed(condition.Entries, value, reading.Budget) is not { } listed)
                {
                    return OutOfWork;
                }

                if (!listed)
                {
                    return $"{Where(condition)} has a value that is not on the rule's list";
                }
            }
        }

        return !present && rule.Conditions.Any(other => other.Kind is RuleConditionKind.ToolIs) ? $"{Where(condition)} is not there to check" : null;
    }

    // A pattern condition: every piece of text in the tool's input, whichever field the model put it in.
    private static string? HasAnAddressOffTheList(RuleCondition condition, RuleReading reading)
    {
        if (reading.IsTooMuch)
        {
            return TooMuch;
        }

        foreach (var piece in reading.Pieces)
        {
            if (!reading.Budget.TrySpend(1) || piece.Text(reading.Budget) is not { } text || HasAnAddressOffTheList(condition.Entries, text, reading.Budget) is not { } off)
            {
                return OutOfWork;
            }

            if (off)
            {
                return "the tool's input has an email address that is not on the rule's list";
            }
        }

        return null;
    }

    // One reading of a text, token by token. Tokens are parted by white space, quotes, angle brackets, commas,
    // semicolons, and parentheses. A token with an @ in it is on the list only when it is exactly a listed address,
    // or when what follows its last @ is a listed host or under one. Any other token with an @ in it is off the
    // list: to let one through, a person lists its host. Null when the budget could not pay for the reading.
    private static bool? HasAnAddressOffTheList(IReadOnlyList<string> allowed, RuleText text, RuleBudget budget)
    {
        var plain = text.Plain;

        if (!budget.TrySpend(plain.Length))
        {
            return null;
        }

        var start = 0;

        for (var index = 0; index <= plain.Length; index++)
        {
            if (index < plain.Length && !IsTokenEnd(plain[index]))
            {
                continue;
            }

            var token = plain.AsSpan(start, index - start);
            start = index + 1;

            // The tool gets the text as written. A token that only reads as a listed address once unseen
            // characters are taken out, or another form of a letter is made plain, is not that address.
            if (token.Contains('@') && (!text.IsAsWritten || !IsListedAddress(allowed, token)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTokenEnd(char character) =>
        char.IsWhiteSpace(character) || character is '"' or '\'' or '<' or '>' or ',' or ';' or '(' or ')';

    // The token without the "mailto:" a link puts before an address and without the full stop, colon, or mark that
    // ends a sentence after one.
    private static bool IsListedAddress(IReadOnlyList<string> allowed, ReadOnlySpan<char> token)
    {
        var address = token.TrimEnd(".:!?").ToString();
        address = address.StartsWith(MailTo, StringComparison.OrdinalIgnoreCase) ? address[MailTo.Length..] : address;
        var host = address[(address.LastIndexOf('@') + 1)..];
        return allowed.Select(entry => entry.Trim()).Any(listed =>
            address.Equals(listed, StringComparison.OrdinalIgnoreCase)
            || (!listed.Contains('@', StringComparison.Ordinal)
                && (host.Equals(listed, StringComparison.OrdinalIgnoreCase) || host.EndsWith($".{listed}", StringComparison.OrdinalIgnoreCase))));
    }

    private static string KindWords(ToolSensitivity kind) => kind switch
    {
        ToolSensitivity.Read => "reads",
        ToolSensitivity.Write => "writes",
        _ => "is critical",
    };

    private static string Where(RuleCondition condition) => condition.Argument is { } argument ? $"the argument {argument}" : "the text";

    private static string? Exceeds(RuleCondition condition, RuleSubject subject)
    {
        var (count, words) = condition.Counter is RunCounter.Tokens ? (subject.Tokens, "tokens") : (subject.ToolCalls, "tool calls");
        return count > (condition.Limit ?? long.MaxValue) ? $"the run has used more than {condition.Limit} {words}" : null;
    }

    private static bool IsAtThisTime(RuleCondition condition, DateTimeOffset now)
    {
        if (!TimeOnly.TryParseExact(condition.From, "HH:mm", out var from)
            || !TimeOnly.TryParseExact(condition.To, "HH:mm", out var to)
            || condition.TimeZone is null
            || !TimeZoneInfo.TryFindSystemTimeZoneById(condition.TimeZone, out var zone))
        {
            // A condition the validator let through always reads. One that does not is treated as holding.
            return true;
        }

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var crossesMidnight = to <= from;

        // After midnight in a span that started the evening before, the day that counts is the one it started on.
        var day = crossesMidnight && time < to ? local.AddDays(-1).DayOfWeek : local.DayOfWeek;
        var within = crossesMidnight ? time >= from || time < to : time >= from && time < to;
        return within && (condition.Weekdays.Count == 0 || condition.Weekdays.Contains(day));
    }

    // On the list as written, or one address or host under a listed domain: "example.org" lists "a@example.org"
    // and "mail.example.org". The value has to be that and nothing more. Two addresses in one value, an address
    // inside a link, or a longer name that only ends like a listed one are not on the list. Neither is a value
    // with characters that cannot be seen or in another form: the tool gets the value as written, so that is
    // what is compared. Null when the budget could not pay for reading the value.
    private static bool? IsListed(IReadOnlyList<string> allowed, string value, RuleBudget budget)
    {
        if (RuleText.Read(value, budget) is not { } read)
        {
            return null;
        }

        if (!read.IsAsWritten)
        {
            return false;
        }

        var plain = value.Trim();
        var at = plain.IndexOf('@', StringComparison.Ordinal);
        var host = at < 0 ? plain : plain[(at + 1)..];
        var isOneName = host.Length > 0 && host.All(IsHostCharacter) && (at < 0 || (at > 0 && plain[..at].All(IsMailboxCharacter)));
        return allowed.Select(entry => entry.Trim()).Any(listed =>
            plain.Equals(listed, StringComparison.OrdinalIgnoreCase)
            || (isOneName
                && !listed.Contains('@', StringComparison.Ordinal)
                && (host.Equals(listed, StringComparison.OrdinalIgnoreCase) || host.EndsWith($".{listed}", StringComparison.OrdinalIgnoreCase))));
    }

    private static bool IsHostCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '.' or '-';

    private static bool IsMailboxCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '%' or '+' or '-';

    // The values under a named input, as text: strings as they are, numbers as written.
    private static IEnumerable<string> Values(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString() ?? string.Empty],
        JsonValueKind.Number => [value.GetRawText()],
        JsonValueKind.Array => value.EnumerateArray().SelectMany(Values),
        JsonValueKind.Object => value.EnumerateObject().SelectMany(property => Values(property.Value)),
        _ => [],
    };

    private static IEnumerable<RuleCondition> TextConditions(ActiveRule rule) =>
        rule.Conditions.Where(condition => condition.Kind is RuleConditionKind.TextContains or RuleConditionKind.TextMatches or RuleConditionKind.TextHolds);

    #endregion

    #region Redaction

    // Why a rule that removes text cannot do so here, as the end of a sentence, or null when it can. What there is
    // too much of, or what could not be searched, cannot be rewritten. Neither can the name of an input, a value
    // that is not text, or an object in which two inputs share a name.
    private static string? CannotRewrite(ActiveRule rule, RuleReading reading)
    {
        if (reading.IsTooMuch || reading.Budget.IsSpent)
        {
            return NothingLetThrough;
        }

        List<RuleSearch> searches = [.. TextConditions(rule).Select(reading.Search)];

        if (reading.Budget.IsSpent)
        {
            return NothingLetThrough;
        }

        return searches.Any(search => search.InAName) ? ", in the name of an input, which a rule cannot rewrite"
            : searches.Any(search => search.InWhatIsNotText) ? ", in a value that is not text, which a rule cannot rewrite"
            : reading.HasSharedNames ? ", among inputs that share a name, which a rule cannot rewrite"
            : null;
    }

    private static bool MatchesInAName(ActiveRule rule, RuleReading reading) =>
        !reading.IsTooMuch && TextConditions(rule).Any(condition => reading.Search(condition).InAName);

    // The subject with every place the redacting rules match replaced, or null when the budget could not pay for
    // the rewriting.
    private static (string? Text, JsonElement? Json)? Rewrite(RuleReading reading, List<ActiveRule> redacting)
    {
        List<RuleSearch> searches = [.. redacting.SelectMany(TextConditions).Select(reading.Search).Distinct()];
        var next = 0;
        var text = reading.Subject.Text is null ? null : Redact(reading, next++, searches);
        var json = reading.Subject.Json is { } value ? RedactNode(value, reading, searches, ref next) : null;
        return reading.Budget.IsSpent ? null : (text, json is null ? null : JsonSerializer.SerializeToElement(json));
    }

    // One piece of text with every place the searches match in it replaced. Rewriting a piece is one more reading.
    private static string Redact(RuleReading reading, int index, List<RuleSearch> searches)
    {
        var text = reading.Pieces[index].Written;
        var places = searches.SelectMany(search => search.Places[index] ?? []).OrderBy(place => place.Start).ToList();

        if (places.Count == 0 || !reading.Budget.TrySpend(text.Length))
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        var cursor = 0;

        foreach (var (start, length) in places)
        {
            // Overlapping matches are replaced once, from where the first begins to where the last ends.
            if (start >= cursor)
            {
                result.Append(text, cursor, start - cursor).Append(Redaction);
            }

            cursor = Math.Max(cursor, start + length);
        }

        return result.Append(text, cursor, text.Length - cursor).ToString();
    }

    // The value with its strings redacted. It is walked in the order it was read, so each string is the next piece.
    // Names, numbers, and flags are kept as they are: a rule that matched in one of those blocked before this.
    private static JsonNode? RedactNode(JsonElement value, RuleReading reading, List<RuleSearch> searches, ref int next)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return JsonValue.Create(Redact(reading, next++, searches));
            case JsonValueKind.Array:
                JsonArray items = [];

                foreach (var item in value.EnumerateArray())
                {
                    items.Add(RedactNode(item, reading, searches, ref next));
                }

                return items;
            case JsonValueKind.Object:
                JsonObject copy = [];

                foreach (var property in value.EnumerateObject())
                {
                    next++;
                    copy[property.Name] = RedactNode(property.Value, reading, searches, ref next);
                }

                return copy;
            default:
                next++;
                return JsonNode.Parse(value.GetRawText());
        }
    }

    #endregion

    #region Words

    private const string MailTo = "mailto:";

    #endregion
}
