namespace Drover.Agents.History;

/// <summary>
/// Translates between the framework's messages and Drover's message parts. Conversation content is Drover's data, so
/// only what Drover's part schema can hold is stored: text, reasoning, tool calls, and tool results. Approval
/// requests and responses are working state; they live in the session and in <c>tool_calls</c>, not in messages.
/// </summary>
public static class MessageParts
{
    #region Public Methods

    /// <summary>
    /// The message as the model reads it. A file part becomes what <paramref name="files"/> holds for it, and a line
    /// saying it could not be opened when it holds nothing.
    /// </summary>
    public static ChatMessage ToChatMessage(this Message message, IReadOnlyDictionary<Guid, FileForModel>? files = null) =>
        new(ToChatRole(message.Role), [.. message.Parts.SelectMany(part => ToContents(part, files))])
        {
            MessageId = message.Id.ToString("N"),
            CreatedAt = message.CreatedAt,
        };

    /// <summary>The ids of the files the messages carry, each once.</summary>
    public static IReadOnlyCollection<Guid> FileIds(this IEnumerable<Message> messages) =>
    [
        .. messages
            .SelectMany(message => message.Parts)
            .Select(part => part switch
            {
                ImagePart image => image.FileId,
                FilePart file => file.FileId,
                _ => Guid.Empty,
            })
            .Where(id => id != Guid.Empty)
            .Distinct(),
    ];

    /// <summary>
    /// Null when the message holds nothing Drover stores. A model's reply keeps the id its model call gave it
    /// (<paramref name="keepReplyId"/>), so the row has the id the run's events already named.
    /// </summary>
    public static Message? ToMessage(this ChatMessage message, Guid runId, Guid botId, Guid principalUserId, bool keepReplyId = false)
    {
        List<MessagePart> parts = [.. message.Contents.Select(content => ToPart(content, runId)).OfType<MessagePart>()];

        if (parts.Count == 0)
        {
            return null;
        }

        var role = ToMessageRole(message.Role);
        return new Message
        {
            Id = keepReplyId && role is MessageRole.Assistant && Guid.TryParseExact(message.MessageId, "N", out var replyId)
                ? replyId
                : Guid.CreateVersion7(),
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

    private static IEnumerable<AIContent> ToContents(MessagePart part, IReadOnlyDictionary<Guid, FileForModel>? files) => part switch
    {
        ImagePart image => ToContents(image.FileId, image.Name ?? "a picture", files),
        FilePart file => ToContents(file.FileId, file.Name, files),
        _ => ToContent(part) is { } content ? [content] : [],
    };

    private static IEnumerable<AIContent> ToContents(Guid fileId, string name, IReadOnlyDictionary<Guid, FileForModel>? files)
    {
        if (files is null || !files.TryGetValue(fileId, out var given))
        {
            yield return new TextContent($"[The person attached {name} here. It could not be opened.]");
            yield break;
        }

        yield return new TextContent(given.Text);

        if (given is { Image: { } image, MediaType: { } mediaType })
        {
            yield return new DataContent(image, mediaType);
        }
    }

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
