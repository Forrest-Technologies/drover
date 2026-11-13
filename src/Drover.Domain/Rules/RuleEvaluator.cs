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
    /// The outcome for a subject, or null when no rule applies. Of the rules that apply, the strictest effect wins
    /// (stop, then block, then ask, then redact), and among equals the first in order. When redaction wins, every
    /// applying redaction is carried out, so one rule's match is not let through because another came first.
    /// </summary>
    public static RuleOutcome? Evaluate(IReadOnlyList<ActiveRule> rules, RuleSubject subject)
    {
        List<(ActiveRule Rule, string Why)> applied = [];

        foreach (var rule in rules)
        {
            if (rule.Checkpoint == subject.Checkpoint && Applies(rule, subject) is { } why)
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

        if (deciding.Effect is not RuleEffect.Redact)
        {
            return outcome;
        }

        var redacting = applied.Select(entry => entry.Rule).ToList();
        return outcome with
        {
            RedactedText = subject.Text is { } text ? Redact(text, redacting, null) : null,
            RedactedJson = subject.Json is { } json ? Redact(json, redacting, null) : null,
        };
    }

    #endregion

    #region Conditions

    // The reason the rule applies, in words that never hold the text that matched, or null when it does not apply.
    private static string? Applies(ActiveRule rule, RuleSubject subject)
    {
        List<string> reasons = [];

        foreach (var condition in rule.Conditions)
        {
            if (Holds(condition, subject) is not { } reason)
            {
                return null;
            }

            reasons.Add(reason);
        }

        return reasons.Count == 0 ? "it always applies" : string.Join(", and ", reasons);
    }

    private static string? Holds(RuleCondition condition, RuleSubject subject) => condition.Kind switch
    {
        RuleConditionKind.ToolIs => subject.ToolName is { } tool && condition.Tools.Contains(tool, StringComparer.Ordinal)
            ? $"the tool is {tool}"
            : null,
        RuleConditionKind.ToolKind => subject.ToolKind is { } kind && condition.ToolKinds.Contains(kind)
            ? $"the tool {KindWords(kind)}"
            : null,
        RuleConditionKind.TextContains => Texts(subject, condition.Argument).Any(text => Places(condition, text).Count > 0)
            ? $"{Where(condition)} contains text the rule names"
            : null,
        RuleConditionKind.TextMatches => Texts(subject, condition.Argument).Any(text => Places(condition, text).Count > 0)
            ? $"{Where(condition)} matches the rule's pattern"
            : null,
        RuleConditionKind.TextHolds => Texts(subject, condition.Argument).Any(text => Places(condition, text).Count > 0)
            ? $"{Where(condition)} holds {RulePatterns.Words(condition.Builtin ?? BuiltinPattern.Key)}"
            : null,
        RuleConditionKind.NotInList => Texts(subject, condition.Argument).Any(text => !IsListed(condition.Values, text))
            ? $"{Where(condition)} has a value that is not on the rule's list"
            : null,
        RuleConditionKind.TimeIs => IsAtThisTime(condition, subject.Now) ? "it is within the rule's hours" : null,
        RuleConditionKind.RunExceeds => Exceeds(condition, subject),
        _ => null,
    };

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

    // On the list as written, or under a listed domain: "example.org" lists "a@example.org" and "mail.example.org".
    private static bool IsListed(IReadOnlyList<string> allowed, string value)
    {
        var trimmed = value.Trim();
        return allowed.Any(listed =>
            trimmed.Equals(listed, StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith($"@{listed}", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith($".{listed}", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Text

    // The pieces of text a condition looks at: the reply or message, or the strings of the tool's arguments or
    // result. With an argument named, only the strings under that argument.
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
            foreach (var leaf in Strings(json))
            {
                yield return leaf;
            }
        }
        else if (json.ValueKind is JsonValueKind.Object && json.TryGetProperty(argument, out var named))
        {
            foreach (var leaf in Strings(named))
            {
                yield return leaf;
            }
        }
    }

    private static IEnumerable<string> Strings(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                yield return value.GetString() ?? string.Empty;
                break;
            case JsonValueKind.Array:
                foreach (var leaf in value.EnumerateArray().SelectMany(Strings))
                {
                    yield return leaf;
                }

                break;
            case JsonValueKind.Object:
                foreach (var leaf in value.EnumerateObject().SelectMany(property => Strings(property.Value)))
                {
                    yield return leaf;
                }

                break;
            case JsonValueKind.Number:
                // A number can be a card number as well as a string can.
                yield return value.GetRawText();
                break;
        }
    }

    // Where a text condition matches in one piece of text.
    private static List<(int Start, int Length)> Places(RuleCondition condition, string text)
    {
        switch (condition.Kind)
        {
            case RuleConditionKind.TextMatches:
                return RulePatterns.Compile(condition.Pattern).Expression is { } expression ? RulePatterns.Find(expression, text) : WholeOf(text);
            case RuleConditionKind.TextHolds:
                return RulePatterns.Find(condition.Builtin ?? BuiltinPattern.Key, text);
            case RuleConditionKind.TextContains:
                List<(int, int)> found = [];

                foreach (var piece in condition.Values.Where(piece => piece.Length > 0))
                {
                    for (var at = text.IndexOf(piece, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(piece, at + piece.Length, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add((at, piece.Length));
                    }
                }

                return found;
            default:
                return [];
        }
    }

    // A pattern that cannot be read is treated as matching everything: a rule fails closed.
    private static List<(int Start, int Length)> WholeOf(string text) => text.Length == 0 ? [] : [(0, text.Length)];

    #endregion

    #region Redaction

    private static string Redact(string text, List<ActiveRule> rules, string? argument)
    {
        var places = rules
            .SelectMany(rule => rule.Conditions)
            .Where(condition => condition.Argument is null || condition.Argument == argument)
            .SelectMany(condition => Places(condition, text))
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

    private static JsonElement Redact(JsonElement value, List<ActiveRule> rules, string? argument) =>
        JsonSerializer.SerializeToElement(RedactNode(value, rules, argument, atTop: true));

    private static JsonNode? RedactNode(JsonElement value, List<ActiveRule> rules, string? argument, bool atTop)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return JsonValue.Create(Redact(value.GetString() ?? string.Empty, rules, argument));
            case JsonValueKind.Number:
                var digits = value.GetRawText();
                var redacted = Redact(digits, rules, argument);
                return redacted == digits ? JsonNode.Parse(digits) : JsonValue.Create(redacted);
            case JsonValueKind.Array:
                return new JsonArray([.. value.EnumerateArray().Select(item => RedactNode(item, rules, argument, atTop: false))]);
            case JsonValueKind.Object:
                JsonObject copy = [];

                foreach (var property in value.EnumerateObject())
                {
                    // An argument is a property of the top object; everything beneath it belongs to it.
                    copy[property.Name] = RedactNode(property.Value, rules, atTop ? property.Name : argument, atTop: false);
                }

                return copy;
            default:
                return JsonNode.Parse(value.GetRawText());
        }
    }

    #endregion
}
