namespace Drover.Adapters.SystemOne;

/// <summary>
/// The request and the answer of the state-and-questions API, the same for every kind. Questions go out under names
/// of this adapter's own (<c>q1</c>, <c>q2</c>, ...), which every provider accepts, and come back under the caller's.
/// No type of the wire leaves this project.
/// </summary>
internal static class SystemOneWire
{
    #region Public Methods

    public static string WireName(int index) => $"q{index + 1}";

    /// <summary>The request for some of the questions: the whole state, and each question under its wire name.</summary>
    public static string Request(string model, DecisionRequest request, IReadOnlyList<(int Index, DecisionQuestion Question)> questions)
    {
        var state = new JsonObject();

        foreach (var (name, text) in request.State)
        {
            state[name] = text;
        }

        var asked = new JsonObject();

        foreach (var (index, question) in questions)
        {
            asked[WireName(index)] = Question(question);
        }

        return new JsonObject { ["model"] = model, ["state"] = state, ["questions"] = asked }.ToJsonString();
    }

    /// <summary>
    /// The answers in a response, by wire name, and the input tokens it used. A question the provider refused, or
    /// answered with something that is not a probability from 0 to 1, has no entry. Null when the response is not
    /// this API at all.
    /// </summary>
    public static (Dictionary<string, DecisionResult> Answers, long InputTokens)? Read(JsonElement root, bool enveloped)
    {
        if (enveloped && root.ValueKind is JsonValueKind.Object && root.TryGetProperty("result", out var result))
        {
            root = result;
        }

        if (root.ValueKind is not JsonValueKind.Object || !root.TryGetProperty("answers", out var answers) || answers.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, DecisionResult> read = [];

        foreach (var answer in answers.EnumerateObject())
        {
            if (Result(answer.Value) is { } parsed)
            {
                read[answer.Name] = parsed;
            }
        }

        return (read, InputTokens(root));
    }

    #endregion

    #region Private Methods

    private static JsonObject Question(DecisionQuestion question)
    {
        if (question.Choices.Count == 0)
        {
            return new JsonObject { ["type"] = "noul", ["instructions"] = question.Text };
        }

        var criteria = new JsonObject();

        foreach (var choice in question.Choices)
        {
            criteria[choice] = choice;
        }

        return new JsonObject { ["type"] = "choice", ["instructions"] = question.Text, ["criteria"] = criteria };
    }

    private static DecisionResult? Result(JsonElement answer)
    {
        if (answer.ValueKind is not JsonValueKind.Object || !answer.TryGetProperty("type", out var type))
        {
            return null;
        }

        return type.GetString() switch
        {
            "noul" when answer.TryGetProperty("noul", out var yes) && Probability(yes) is { } probability => new DecisionResult { Yes = probability },
            "choice" when answer.TryGetProperty("probabilities", out var probabilities) && Probabilities(probabilities) is { Count: > 0 } choices =>
                new DecisionResult { Choices = choices },
            _ => null,
        };
    }

    private static Dictionary<string, double>? Probabilities(JsonElement probabilities)
    {
        if (probabilities.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, double> read = [];

        foreach (var choice in probabilities.EnumerateObject())
        {
            if (Probability(choice.Value) is not { } probability)
            {
                return null;
            }

            read[choice.Name] = probability;
        }

        return read;
    }

    private static double? Probability(JsonElement value) =>
        value.ValueKind is JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number is >= 0 and <= 1
            ? number
            : null;

    private static long InputTokens(JsonElement root) =>
        root.TryGetProperty("usage", out var usage)
        && usage.ValueKind is JsonValueKind.Object
        && usage.TryGetProperty("input_tokens", out var tokens)
        && tokens.TryGetInt64(out var count)
            ? Math.Max(0, count)
            : 0;

    #endregion
}
