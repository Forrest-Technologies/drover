namespace Drover.Domain.Rules;

/// <summary>
/// Decides which rule applies to a subject. This is the one place rules are evaluated: a run and the editor's
/// "Try it" both call it, so what a person is shown is what a run does. Same input, same outcome; no model is asked.
/// </summary>
public static class RuleEvaluator
{
    #region Private Fields

    public const string Redaction = "[removed by a rule]";

    #endregion

    #region Public Methods

    /// <summary>
    /// The outcome for a subject, or null when no rule applies. Of the rules that apply, the strictest effect decides
    /// (stop, then block, then ask, then redact), and among equals the first in order. Effects hold together: every
    /// applying rule that redacts is carried out whichever rule decides, so a rule that asks never lets through what
    /// another rule removes. The whole evaluation shares one time budget for searching text.
    /// </summary>
    public static RuleOutcome? Evaluate(IReadOnlyList<ActiveRule> rules, RuleSubject subject) =>
        Evaluate(rules, subject, new RuleBudget(RuleBudget.Default));

    public static RuleOutcome? Evaluate(IReadOnlyList<ActiveRule> rules, RuleSubject subject, RuleBudget budget)
    {
        List<(ActiveRule Rule, string Why)> applied = [];

        foreach (var rule in rules)
        {
            if (rule.Checkpoint == subject.Checkpoint && Applies(rule, subject, budget) is { } why)
            {
                applied.Add((rule, why));
            }
        }

        if (applied.Count == 0)
        {
            return null;
        }

        var (deciding, reason) = applied.MaxBy(entry => entry.Rule.Effect);
        var outcome = new RuleOutcome(deciding, deciding.Effect, reason)
        {
            Applied = [deciding, .. applied.Select(entry => entry.Rule).Where(rule => rule != deciding)],
        };

        var redacting = applied.Select(entry => entry.Rule).Where(rule => rule.Effect is RuleEffect.Redact).ToList();

        if (redacting.Count == 0)
        {
            return outcome;
        }

        return outcome with
        {
            RedactedText = subject.Text is { } text ? Redact(text, redacting, null, budget) : null,
            RedactedJson = subject.Json is { } json ? Redact(json, redacting, budget) : null,
        };
    }

    #endregion

    #region Conditions

    // The reason the rule applies, in words that never hold the text that matched, or null when it does not apply.
    private static string? Applies(ActiveRule rule, RuleSubject subject, RuleBudget budget)
    {
        List<string> reasons = [];

        foreach (var condition in rule.Conditions)
        {
            if (Holds(condition, subject, budget) is not { } reason)
            {
                return null;
            }

            reasons.Add(reason);
        }

        return reasons.Count == 0 ? "it always applies" : string.Join(", and ", reasons);
    }

    private static string? Holds(RuleCondition condition, RuleSubject subject, RuleBudget budget) => condition.Kind switch
    {
        RuleConditionKind.ToolIs => subject.ToolName is { } tool && condition.Tools.Contains(tool, StringComparer.OrdinalIgnoreCase)
            ? $"the tool is {tool}"
            : null,
        RuleConditionKind.ToolKind => subject.ToolKind is { } kind && condition.ToolKinds.Contains(kind)
            ? $"the tool {KindWords(kind)}"
            : null,
        RuleConditionKind.TextContains => Texts(subject, condition.Argument).Any(text => Places(condition, text, budget).Count > 0)
            ? $"{Where(condition)} contains text the rule names"
            : null,
        RuleConditionKind.TextMatches => Texts(subject, condition.Argument).Any(text => Places(condition, text, budget).Count > 0)
            ? $"{Where(condition)} matches the rule's pattern"
            : null,
        RuleConditionKind.TextHolds => Texts(subject, condition.Argument).Any(text => Places(condition, text, budget).Count > 0)
            ? $"{Where(condition)} holds {RulePatterns.Words(condition.Builtin ?? BuiltinPattern.Key)}"
            : null,
        RuleConditionKind.NotInList => IsOffTheList(condition, subject),
        RuleConditionKind.TimeIs => IsAtThisTime(condition, subject.Now) ? "it is within the rule's hours" : null,
        RuleConditionKind.RunExceeds => Exceeds(condition, subject),
        _ => null,
    };

    // A list rule names the inputs it checks. Every value of every named input that is there has to be on the list.
    // When none of them is there the rule has nothing to pass, and applies: sending by another field is not a way
    // around the list.
    private static string? IsOffTheList(RuleCondition condition, RuleSubject subject)
    {
        if (subject.Json is not { ValueKind: JsonValueKind.Object } json)
        {
            return $"{Where(condition)} is not there to check";
        }

        var present = Names(condition.Argument).Where(name => json.TryGetProperty(name, out _)).ToList();

        if (present.Count == 0)
        {
            return $"{Where(condition)} is not there to check";
        }

        return present.SelectMany(name => Strings(json.GetProperty(name), names: false)).Any(value => !IsListed(condition.Entries, value))
            ? $"{Where(condition)} has a value that is not on the rule's list"
            : null;
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
    // inside a link, or a longer name that only ends like a listed one are not on the list.
    private static bool IsListed(IReadOnlyList<string> allowed, string value)
    {
        var plain = new RuleText(value).Plain.Trim();
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

    #endregion

    #region Text

    // The pieces of text a condition looks at: the reply or message, or the strings of the tool's arguments or
    // result, the names of their properties included, because a model writes those too. With inputs named, only
    // what is under those inputs.
    private static IEnumerable<string> Texts(RuleSubject subject, string? argument)
    {
        if (subject.Text is { } text)
        {
            yield return text;
        }

        if (subject.Json is not { } json)
        {
            yield break;
        }

        if (argument is null)
        {
            foreach (var leaf in Strings(json, names: true))
            {
                yield return leaf;
            }
        }
        else if (json.ValueKind is JsonValueKind.Object)
        {
            foreach (var leaf in Names(argument).SelectMany(name => json.TryGetProperty(name, out var named) ? Strings(named, names: true) : []))
            {
                yield return leaf;
            }
        }
    }

    private static IEnumerable<string> Strings(JsonElement value, bool names)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                yield return value.GetString() ?? string.Empty;
                break;
            case JsonValueKind.Array:
                foreach (var leaf in value.EnumerateArray().SelectMany(item => Strings(item, names)))
                {
                    yield return leaf;
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    if (names)
                    {
                        yield return property.Name;
                    }

                    foreach (var leaf in Strings(property.Value, names))
                    {
                        yield return leaf;
                    }
                }

                break;
            case JsonValueKind.Number:
                // A number can be a card number as well as a string can.
                yield return value.GetRawText();
                break;
        }
    }

    // Where a text condition matches in one piece of text, as places in the text as it was written. The search
    // itself runs on the text's plain form, so an unseen character or another form of a letter hides nothing.
    private static List<(int Start, int Length)> Places(RuleCondition condition, string written, RuleBudget budget)
    {
        if (written.Length == 0)
        {
            return [];
        }

        if (budget.IsSpent)
        {
            return WholeOf(written);
        }

        var text = new RuleText(written);
        return [.. PlacesInPlain(condition, text.Plain, budget).Select(place => text.InOriginal(place.Start, place.Length))];
    }

    private static List<(int Start, int Length)> PlacesInPlain(RuleCondition condition, string text, RuleBudget budget)
    {
        switch (condition.Kind)
        {
            case RuleConditionKind.TextMatches:
                return RulePatterns.Compile(condition.Pattern).Expression is { } expression ? RulePatterns.Find(expression, text, budget) : WholeOf(text);
            case RuleConditionKind.TextHolds:
                return RulePatterns.Find(condition.Builtin ?? BuiltinPattern.Key, text, budget);
            case RuleConditionKind.TextContains:
                using (budget.Search(text.Length))
                {
                    List<(int, int)> found = [];

                    foreach (var piece in condition.Entries.Select(entry => new RuleText(entry).Plain).Where(piece => piece.Length > 0))
                    {
                        for (var at = text.IndexOf(piece, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(piece, at + piece.Length, StringComparison.OrdinalIgnoreCase))
                        {
                            found.Add((at, piece.Length));
                        }

                        if (budget.IsSpent)
                        {
                            return WholeOf(text);
                        }
                    }

                    return found;
                }

            default:
                return [];
        }
    }

    // A pattern that cannot be read is treated as matching everything: a rule fails closed.
    private static List<(int Start, int Length)> WholeOf(string text) => text.Length == 0 ? [] : [(0, text.Length)];

    #endregion

    #region Redaction

    private static string Redact(string text, List<ActiveRule> rules, string? argument, RuleBudget budget)
    {
        var places = rules
            .SelectMany(rule => rule.Conditions)
            .Where(condition => condition.Argument is null || Names(condition.Argument).Contains(argument, StringComparer.Ordinal))
            .SelectMany(condition => Places(condition, text, budget))
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

    private static JsonElement Redact(JsonElement value, List<ActiveRule> rules, RuleBudget budget) =>
        JsonSerializer.SerializeToElement(RedactNode(value, rules, null, atTop: true, budget));

    private static JsonNode? RedactNode(JsonElement value, List<ActiveRule> rules, string? argument, bool atTop, RuleBudget budget)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return JsonValue.Create(Redact(value.GetString() ?? string.Empty, rules, argument, budget));
            case JsonValueKind.Number:
                var digits = value.GetRawText();
                var redacted = Redact(digits, rules, argument, budget);
                return redacted == digits ? JsonNode.Parse(digits) : JsonValue.Create(redacted);
            case JsonValueKind.Array:
                return new JsonArray([.. value.EnumerateArray().Select(item => RedactNode(item, rules, argument, atTop: false, budget))]);
            case JsonValueKind.Object:
                JsonObject copy = [];

                foreach (var property in value.EnumerateObject())
                {
                    // An argument is a property of the top object; everything beneath it belongs to it. A name is
                    // text the model wrote as well, so what matches in a name is removed from the name.
                    var under = atTop ? property.Name : argument;
                    var name = Redact(property.Name, rules, atTop ? null : argument, budget);

                    while (copy.ContainsKey(name))
                    {
                        name = $"{name} ";
                    }

                    copy[name] = RedactNode(property.Value, rules, under, atTop: false, budget);
                }

                return copy;
            default:
                return JsonNode.Parse(value.GetRawText());
        }
    }

    #endregion
}
