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

    private const string OutOfTime = "it could not be checked in time";

    private static readonly JsonElement nothing = JsonElement.Parse("{}");

    #endregion

    #region Public Methods

    /// <summary>
    /// The outcome for a subject, or null when no rule applies. Of the rules that apply, the strictest effect decides
    /// (stop, then block, then ask, then redact), and among equals the first in order. Effects hold together: every
    /// applying rule that redacts is carried out whichever rule decides. A rule removes text from text; where what
    /// matched cannot be rewritten (the name of an input, a number, something too long or too slow to read), the
    /// rule blocks. The whole evaluation shares one budget of time and one cap on size.
    /// </summary>
    public static RuleOutcome? Evaluate(IReadOnlyList<ActiveRule> rules, RuleSubject subject) =>
        Evaluate(rules, subject, new RuleBudget(RuleBudget.Default));

    public static RuleOutcome? Evaluate(IReadOnlyList<ActiveRule> rules, RuleSubject subject, RuleBudget budget)
    {
        var reading = new RuleReading(subject, budget);
        List<(ActiveRule Rule, string Why, RuleEffect Effect)> applied = [];

        foreach (var rule in rules)
        {
            if (rule.Checkpoint != subject.Checkpoint || Applies(rule, reading) is not { } why)
            {
                continue;
            }

            applied.Add(rule.Effect is RuleEffect.Redact && CannotRewrite(rule, reading) is { } obstacle
                ? (rule, $"{why}{obstacle}", RuleEffect.Block)
                : (rule, why, rule.Effect));
        }

        if (applied.Count == 0)
        {
            return null;
        }

        var (deciding, reason, effect) = applied.MaxBy(entry => entry.Effect);
        var outcome = new RuleOutcome(deciding, effect, reason)
        {
            Applied = [deciding, .. applied.Select(entry => entry.Rule).Where(rule => rule != deciding)],
        };

        var redacting = applied.Select(entry => entry.Rule).Where(rule => rule.Effect is RuleEffect.Redact).ToList();

        if (redacting.Count == 0)
        {
            return outcome;
        }

        // When any redacting rule met something it cannot rewrite, nothing is kept: what is stored of the subject
        // must not hold what matched, and nobody can say what a clean version would be.
        if (redacting.Any(rule => CannotRewrite(rule, reading) is not null))
        {
            return outcome with
            {
                RedactedText = subject.Text is null ? null : Redaction,
                RedactedJson = subject.Json is null ? null : nothing,
            };
        }

        var next = subject.Text is null ? 0 : 1;
        return outcome with
        {
            RedactedText = subject.Text is null ? null : Redact(reading, 0, redacting),
            RedactedJson = subject.Json is { } json ? JsonSerializer.SerializeToElement(RedactNode(json, reading, redacting, ref next)) : null,
        };
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
    // for size or for time, counts as found.
    private static string? Finds(RuleCondition condition, RuleReading reading, string found)
    {
        if (reading.IsTooMuch)
        {
            return TooMuch;
        }

        for (var index = 0; index < reading.Pieces.Count; index++)
        {
            if (InScope(condition, reading.Pieces[index]) && reading.Places(condition, index).Count > 0)
            {
                return reading.RanOutOfTime ? OutOfTime : found;
            }
        }

        return null;
    }

    // An exact condition. The rule names inputs, matched without regard to case, and every value of every one of
    // them that is in the call has to be on the list, each time it occurs. When none of them is there and the rule
    // says which tools it is about, it applies, because the input should have been.
    private static string? IsOffTheList(RuleCondition condition, ActiveRule rule, RuleReading reading)
    {
        if (reading.Budget.IsSpent)
        {
            return OutOfTime;
        }

        var names = Names(condition.Argument);
        List<JsonElement> present =
        [
            .. reading.Subject.Json is { ValueKind: JsonValueKind.Object } json
                ? json.EnumerateObject().Where(property => names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)).Select(property => property.Value)
                : [],
        ];

        if (present.Count == 0)
        {
            return rule.Conditions.Any(other => other.Kind is RuleConditionKind.ToolIs) ? $"{Where(condition)} is not there to check" : null;
        }

        return present.SelectMany(Values).Any(value => !IsListed(condition.Entries, value))
            ? $"{Where(condition)} has a value that is not on the rule's list"
            : null;
    }

    // A pattern condition: every email address anywhere in the tool's input, whichever field the model put it in.
    private static string? HasAnAddressOffTheList(RuleCondition condition, RuleReading reading)
    {
        if (reading.IsTooMuch)
        {
            return TooMuch;
        }

        foreach (var piece in reading.Pieces)
        {
            if (reading.Budget.IsSpent)
            {
                return OutOfTime;
            }

            if (HasAnAddressOffTheList(condition.Entries, piece.Text))
            {
                return "the tool's input has an email address that is not on the rule's list";
            }
        }

        return null;
    }

    // Looks at what stands around each @ in a piece of text. Something shaped like an address (a name, then a host
    // that ends in a word) has to be a plain address on the list. A shape people use to get an address past a
    // reader (percent-encoding, a quoted name, an address in brackets, letters of another script, signs a mailbox
    // does not have, characters that cannot be seen) is not on any list. An @ that is not part of an address, as in
    // a package version or a mention, is nothing.
    private static bool HasAnAddressOffTheList(IReadOnlyList<string> allowed, RuleText text)
    {
        var plain = text.Plain.Contains('%', StringComparison.Ordinal) ? Uri.UnescapeDataString(text.Plain) : text.Plain;

        for (var at = plain.IndexOf('@', StringComparison.Ordinal); at >= 0; at = plain.IndexOf('@', at + 1))
        {
            var start = at;

            while (start > 0 && !IsAddressEnd(plain[start - 1]))
            {
                start--;
            }

            var end = at + 1;

            while (end < plain.Length && !IsAddressEnd(plain[end]) && plain[end] is not ':')
            {
                end++;
            }

            var host = plain[(at + 1)..end].TrimEnd('.');
            var quoted = at > 0 && plain[at - 1] is '"' or '\'';
            var bracketed = at + 1 < plain.Length && plain[at + 1] is '[';

            if (quoted || bracketed)
            {
                return true;
            }

            if (start == at || !EndsInAWord(host))
            {
                continue;
            }

            // The tool gets the text as written. An address that only reads as one once it is decoded, or once
            // unseen characters are taken out, is not the address a person put on the list.
            if (!text.IsAsWritten || !ReferenceEquals(plain, text.Plain) || !IsListed(allowed, $"{plain[start..at]}@{host}"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAddressEnd(char character) =>
        char.IsWhiteSpace(character) || character is '<' or '>' or '(' or ')' or ',' or ';' or '"' or '\'' or '=' or '?' or '&' or '/' or '\\' or '|' or '[' or ']' or '{' or '}' or '#' or '`';

    // A host ends in a label of letters: "example.org" does, "20.1.0" and "host" do not.
    private static bool EndsInAWord(string host)
    {
        var dot = host.LastIndexOf('.');
        return dot > 0 && host.Length - dot > 2 && host[(dot + 1)..].All(char.IsLetter);
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
    // what is compared.
    private static bool IsListed(IReadOnlyList<string> allowed, string value)
    {
        if (!new RuleText(value).IsAsWritten)
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

    // The inputs a condition names: one, or several parted by commas.
    private static string[] Names(string? argument) =>
        argument?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [];

    // The values under a named input, as text: strings as they are, numbers as written.
    private static IEnumerable<string> Values(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString() ?? string.Empty],
        JsonValueKind.Number => [value.GetRawText()],
        JsonValueKind.Array => value.EnumerateArray().SelectMany(Values),
        JsonValueKind.Object => value.EnumerateObject().SelectMany(property => Values(property.Value)),
        _ => [],
    };

    // A condition with inputs named looks only at what is under those inputs. Without, at everything.
    private static bool InScope(RuleCondition condition, RulePiece piece) =>
        condition.Argument is null || (piece.Argument is { } under && Names(condition.Argument).Contains(under, StringComparer.OrdinalIgnoreCase));

    #endregion

    #region Redaction

    // Why a rule that removes text cannot do so here, as the end of a sentence, or null when it can. What is too
    // long or too slow to read cannot be rewritten; neither can the name of an input or a value that is not text.
    private static string? CannotRewrite(ActiveRule rule, RuleReading reading)
    {
        if (reading.IsTooMuch || reading.RanOutOfTime || reading.Budget.IsSpent)
        {
            return ", so nothing of it is let through";
        }

        var kinds = rule.Conditions
            .Where(condition => condition.Kind is RuleConditionKind.TextContains or RuleConditionKind.TextMatches or RuleConditionKind.TextHolds)
            .SelectMany(condition => Enumerable.Range(0, reading.Pieces.Count)
                .Where(index => reading.Pieces[index].Kind is RulePieceKind.Name or RulePieceKind.Other
                    && InScope(condition, reading.Pieces[index])
                    && reading.Places(condition, index).Count > 0)
                .Select(index => reading.Pieces[index].Kind))
            .ToHashSet();

        if (reading.RanOutOfTime)
        {
            return ", so nothing of it is let through";
        }

        return kinds.Contains(RulePieceKind.Name) ? ", in the name of an input, which a rule cannot rewrite"
            : kinds.Contains(RulePieceKind.Other) ? ", in a value that is not text, which a rule cannot rewrite"
            : null;
    }

    // One piece of text with every place the rules match in it replaced.
    private static string Redact(RuleReading reading, int index, List<ActiveRule> rules)
    {
        var piece = reading.Pieces[index];
        var text = piece.Written;
        var places = rules
            .SelectMany(rule => rule.Conditions)
            .Where(condition => condition.Kind is RuleConditionKind.TextContains or RuleConditionKind.TextMatches or RuleConditionKind.TextHolds && InScope(condition, piece))
            .SelectMany(condition => reading.Places(condition, index))
            .OrderBy(place => place.Start)
            .ToList();

        if (places.Count == 0)
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
    private static JsonNode? RedactNode(JsonElement value, RuleReading reading, List<ActiveRule> rules, ref int next)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return JsonValue.Create(Redact(reading, next++, rules));
            case JsonValueKind.Array:
                JsonArray items = [];

                foreach (var item in value.EnumerateArray())
                {
                    items.Add(RedactNode(item, reading, rules, ref next));
                }

                return items;
            case JsonValueKind.Object:
                JsonObject copy = [];

                foreach (var property in value.EnumerateObject())
                {
                    next++;
                    copy[property.Name] = RedactNode(property.Value, reading, rules, ref next);
                }

                return copy;
            default:
                next++;
                return JsonNode.Parse(value.GetRawText());
        }
    }

    #endregion
}
