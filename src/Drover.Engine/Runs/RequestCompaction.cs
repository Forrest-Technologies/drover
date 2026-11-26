namespace Drover.Engine.Runs;

/// <summary>Fits the outgoing copy of a conversation to an estimated input budget. Stored messages stay intact.</summary>
public static class RequestCompaction
{
    #region Public Methods

    public static ModelChatRequest Fit(ModelChatRequest request, ModelCapabilities? model, MemorySettings memory)
    {
        var context = model?.ContextTokens is > 0 ? model.ContextTokens.Value : 32_768;
        var reserve = request.Options.MaxOutputTokens ?? Math.Min(model?.MaxOutputTokens ?? 4096, context / 4);
        var available = Math.Max(1, context - reserve);
        var target = Math.Min(available, memory.CompactAtTokens ?? available);
        var messages = request.Messages.ToArray();
        var sizes = messages.Select(Size).ToArray();
        var total = sizes.Sum() + TextSize(request.Options.Instructions) + TextSize(request.Options.ResponseSchema?.GetRawText());
        foreach (var tool in request.Options.Tools ?? [])
        {
            total += TextSize(tool.Name) + TextSize(tool.Description) + TextSize(tool.Schema.GetRawText());
        }

        if (total <= target)
        {
            return request;
        }

        var firstUser = Array.FindIndex(messages, message => message.Role == "user");
        var lastUser = Array.FindLastIndex(messages, message => message.Role == "user");
        var protectedFrom = Math.Max(0, messages.Length - Math.Max(1, memory.KeepRecentMessages ?? 1));
        var keep = Enumerable.Repeat(true, messages.Length).ToArray();

        // Keep an assistant tool request and all its following tool replies as one unit.
        for (var start = 0; start < protectedFrom && total > target;)
        {
            var end = start + 1;
            while (end < messages.Length && messages[end].Role == "tool")
            {
                end++;
            }

            if (end <= protectedFrom && !(start <= firstUser && firstUser < end) && !(start <= lastUser && lastUser < end)
                && messages[start].Role is "user" or "assistant")
            {
                for (var index = start; index < end; index++)
                {
                    keep[index] = false;
                    total -= sizes[index];
                }
            }

            start = end;
        }

        // A single large tool result can fill the window during the current turn.
        for (var index = 0; index < messages.Length && total > target; index++)
        {
            if (!keep[index] || messages[index].Role != "tool")
            {
                continue;
            }

            var contents = messages[index].Contents.Select(ShortenResult).ToArray();
            messages[index] = messages[index] with { Contents = contents };
            total += Size(messages[index]) - sizes[index];
        }

        if (total > available && model?.ContextTokens is > 0)
        {
            throw ModelProviderException.FromRefusal("The current request is too large for the model after shortening earlier history. Send a smaller message or file, or choose a model with a larger context.");
        }

        return request with { Messages = [.. messages.Where((_, index) => keep[index])] };
    }

    #endregion

    #region Private Methods

    private static ModelContent ShortenResult(ModelContent content)
    {
        if (content is ModelToolResult result && result.Result.GetRawText() is { Length: > 2048 } text)
        {
            var excerpt = $"{text[..896]}\n[Drover shortened this tool result for the model.]\n{text[^896..]}";
            return result with { Result = JsonSerializer.SerializeToElement(excerpt) };
        }

        return content;
    }

    private static long Size(ModelChatMessage message) => 4L + message.Contents.Sum(content => content switch
    {
        ModelText text => TextSize(text.Text),
        ModelReasoning reasoning => TextSize(reasoning.Text) + TextSize(reasoning.ProtectedData),
        ModelToolCall call => TextSize(call.Name) + TextSize(call.CallId) + TextSize(call.Arguments.GetRawText()),
        ModelToolResult result => TextSize(result.CallId) + TextSize(result.Result.GetRawText()),
        ModelData or ModelUri => 1600L,
        _ => 0L,
    });

    private static long TextSize(string? text) => ((text?.Length ?? 0) + 2L) / 3;

    #endregion
}
