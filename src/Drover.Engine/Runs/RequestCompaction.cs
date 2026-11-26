namespace Drover.Engine.Runs;

/// <summary>Fits a copy of the final provider request. Nothing in the transcript or framework session is rewritten.</summary>
public static class RequestCompaction
{
    #region Private Fields

    private static readonly ModelChatMessage notice = new("user", [new ModelText("Earlier parts of this conversation were left out to fit this model.")]);

    #endregion

    #region Public Methods

    public static ModelChatRequest Fit(ModelChatRequest request, string model, long context, int reserve, int keepRecent, double calibration)
    {
        var raw = TokenEstimate.Request(request);
        var target = Math.Max(1, context - reserve) / calibration;
        if (raw <= target)
        {
            return request;
        }

        var messages = request.Messages.ToArray();
        var first = Array.FindIndex(messages, IsPerson);
        var last = Array.FindLastIndex(messages, IsPerson);
        var keepFrom = Math.Max(0, messages.Length - Math.Max(1, keepRecent));
        var retained = Enumerable.Repeat(true, messages.Length).ToArray();
        var remaining = raw + TokenEstimate.Message(notice);

        for (var i = 0; i < keepFrom && remaining > target; i++)
        {
            var message = messages[i];
            if (message.Role != "tool")
            {
                continue;
            }

            messages[i] = message with { Contents = [.. message.Contents.Select(ShortenResult)] };
            remaining += TokenEstimate.Message(messages[i]) - TokenEstimate.Message(message);
        }

        for (var i = 0; i < keepFrom && remaining > target; i++)
        {
            var message = messages[i];
            if (message.Role == "assistant" && message.Contents.Any(part => part is ModelReasoning))
            {
                messages[i] = message with { Contents = [.. message.Contents.Where(part => part is not ModelReasoning)] };
                remaining += TokenEstimate.Message(messages[i]) - TokenEstimate.Message(message);
                if (messages[i].Contents.Count == 0)
                {
                    retained[i] = false;
                    remaining -= TokenEstimate.Message(messages[i]);
                }
            }
        }

        for (var start = 0; start < keepFrom && remaining > target;)
        {
            var end = start + 1;
            while (end < messages.Length && messages[end].Role == "tool")
            {
                end++;
            }

            if (end <= keepFrom && start != first && start != last && messages[start].Role is "assistant" or "user" && !IsContext(messages[start]))
            {
                for (var i = start; i < end; i++)
                {
                    if (retained[i])
                    {
                        retained[i] = false;
                        remaining -= TokenEstimate.Message(messages[i]);
                    }
                }
            }

            start = end;
        }

        var changed = remaining < raw + TokenEstimate.Message(notice);
        var estimate = (long)Math.Ceiling((changed ? remaining : raw) * calibration);
        if (estimate > context)
        {
            throw TooLong(model, estimate, context);
        }

        if (!changed)
        {
            return request;
        }

        var kept = messages.Where((_, index) => retained[index]).ToList();
        var leadingInstructions = kept.TakeWhile(message => message.Role is "system" or "developer").Count();
        kept.Insert(leadingInstructions, notice);
        return request with { Messages = kept };
    }

    public static ModelProviderException TooLong(string model, long estimate, long context) =>
        new(ModelFailure.TooLong, $"The last message and its files are longer than {model} can read ({estimate:N0} of {context:N0} tokens). Send less at once, or choose a model with a larger context.");

    #endregion

    #region Private Methods

    private static bool IsPerson(ModelChatMessage message) => message.Role == "user" && !IsContext(message);

    private static bool IsContext(ModelChatMessage message) => message.Contents.OfType<ModelText>().Any(part =>
        part.Text.StartsWith("[Project files]", StringComparison.Ordinal) ||
        part.Text.StartsWith("[Summary of the conversation so far]", StringComparison.Ordinal) ||
        part.Text.StartsWith("[Notes you kept about this person]", StringComparison.Ordinal));

    private static ModelContent ShortenResult(ModelContent content)
    {
        if (content is ModelToolResult result && result.Result.GetRawText() is { Length: > 100 } text)
        {
            return result with { Result = JsonSerializer.SerializeToElement($"[A result of {text.Length} characters was left out to make room.]") };
        }

        return content;
    }

    #endregion
}
