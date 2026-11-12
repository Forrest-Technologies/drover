namespace Drover.Adapters.Tests.Agents;

/// <summary>
/// A chat client that plays a <see cref="ScriptedModel"/>. The framework ships no public fake, so Drover keeps this
/// one; it streams each turn as a provider would and reports what every request contained.
/// </summary>
public sealed class ScriptedChatClient(ScriptedModel model) : IChatClient
{
    #region Interface Implementations

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        List<ChatResponseUpdate> updates = [];

        await foreach (var update in GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            updates.Add(update);
        }

        return updates.ToChatResponse();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var turn = model.NextTurn([.. messages.SelectMany(Seen)]);
        var messageId = Guid.NewGuid().ToString("N");

        foreach (var output in turn)
        {
            if (output is PauseOutput)
            {
                model.SignalPaused();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            yield return new ChatResponseUpdate(ChatRole.Assistant, [ToContent(output)]) { MessageId = messageId };
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 })])
        {
            MessageId = messageId,
            FinishReason = turn.Any(output => output is ToolCallOutput) ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    #endregion

    #region Private Methods

    private static AIContent ToContent(ModelOutput output) => output switch
    {
        TextOutput text => new TextContent(text.Text),
        ToolCallOutput call => new FunctionCallContent(
            call.CallId,
            call.ToolName,
            JsonSerializer.Deserialize<Dictionary<string, object?>>(call.ArgumentsJson)),
        _ => throw new InvalidOperationException($"Unknown output {output}."),
    };

    private static IEnumerable<SeenMessage> Seen(ChatMessage message)
    {
        var role = message.Role.Value;

        foreach (var content in message.Contents)
        {
            var summary = content switch
            {
                TextContent text => text.Text,
                FunctionCallContent call => $"call {call.CallId} {call.Name}",
                FunctionResultContent result => $"{result.CallId} = {MessageJson(result.Result)}",
                _ => null,
            };

            if (summary is not null)
            {
                yield return new SeenMessage(role, summary);
            }
        }
    }

    /// <summary>Compact JSON, however the value arrived, so tests compare content and not formatting.</summary>
    private static string MessageJson(object? value) =>
        JsonSerializer.Serialize(JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions));

    #endregion
}
