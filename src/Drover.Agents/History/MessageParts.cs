namespace Drover.Agents.History;

/// <summary>
/// Translates between the framework's messages and Drover's message parts. Conversation content is Drover's data, so
/// only what Drover's part schema can hold is stored: text, reasoning, tool calls, and tool results. Approval
/// requests and responses are working state; they live in the session and in <c>tool_calls</c>, not in messages.
/// </summary>
public static class MessageParts
{
    #region Public Methods

    public static ChatMessage ToChatMessage(this Message message) =>
        new(ToChatRole(message.Role), [.. message.Parts.Select(ToContent).OfType<AIContent>()])
        {
            MessageId = message.Id.ToString("N"),
            CreatedAt = message.CreatedAt,
        };

    /// <summary>Null when the message holds nothing Drover stores.</summary>
    public static Message? ToMessage(this ChatMessage message, Guid runId, Guid botId, Guid principalUserId)
    {
        List<MessagePart> parts = [.. message.Contents.Select(content => ToPart(content, runId)).OfType<MessagePart>()];

        if (parts.Count == 0)
        {
            return null;
        }

        var role = ToMessageRole(message.Role);
        return new Message
        {
            Role = role,
            RunId = runId,
            AuthorBotId = role is MessageRole.Assistant ? botId : null,
            AuthorUserId = role is MessageRole.User ? principalUserId : null,
            Parts = parts,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    public static JsonElement ToJson(object? value) =>
        value is JsonElement element ? element : JsonSerializer.SerializeToElement(value, AIJsonUtilities.DefaultOptions);

    #endregion

    #region Private Methods

    private static AIContent? ToContent(MessagePart part) => part switch
    {
        TextPart text => new TextContent(text.Text),
        ReasoningPart reasoning => new TextReasoningContent(reasoning.Text),
        ToolCallPart call => new FunctionCallContent(call.CallId, call.ToolName, ToArguments(call.Arguments)),
        ToolResultPart result => new FunctionResultContent(result.CallId, result.Result),
        _ => null,
    };

    private static MessagePart? ToPart(AIContent content, Guid runId) => content switch
    {
        TextContent { Text.Length: > 0 } text => new TextPart(text.Text),
        TextReasoningContent { Text.Length: > 0 } reasoning => new ReasoningPart(reasoning.Text),
        FunctionCallContent call => new ToolCallPart(
            ToolCallIds.For(runId, call.CallId),
            call.CallId,
            call.Name,
            ToJson(call.Arguments ?? new Dictionary<string, object?>())),
        FunctionResultContent result => new ToolResultPart(
            ToolCallIds.For(runId, result.CallId),
            result.CallId,
            result.Exception is not null,
            ToJson(result.Result)),
        _ => null,
    };

    private static Dictionary<string, object?> ToArguments(JsonElement arguments) =>
        arguments.ValueKind is JsonValueKind.Object
            ? arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone())
            : [];

    private static ChatRole ToChatRole(MessageRole role) => role switch
    {
        MessageRole.System => ChatRole.System,
        MessageRole.User => ChatRole.User,
        MessageRole.Assistant => ChatRole.Assistant,
        _ => ChatRole.Tool,
    };

    private static MessageRole ToMessageRole(ChatRole role)
    {
        if (role == ChatRole.System)
        {
            return MessageRole.System;
        }

        if (role == ChatRole.User)
        {
            return MessageRole.User;
        }

        return role == ChatRole.Tool ? MessageRole.Tool : MessageRole.Assistant;
    }

    #endregion
}
