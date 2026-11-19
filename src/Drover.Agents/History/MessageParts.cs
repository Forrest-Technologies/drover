namespace Drover.Agents.History;

/// <summary>
/// Translates between the framework's messages and Drover's message parts. Conversation content is Drover's data, so
/// only what Drover's part schema can hold is stored: text, reasoning, the sources a reply cites, tool calls, and tool
/// results. Sources are shown to people and not given back to a model. Approval
/// requests and responses are working state; they live in the session and in <c>tool_calls</c>, not in messages.
/// </summary>
public static class MessageParts
{
    #region Public Methods

    /// <summary>
    /// The message as the model reads it. A file part a person sent becomes what <paramref name="files"/> holds for
    /// it, and a line saying it could not be opened when it holds nothing. A file part in a bot's reply is a file the
    /// bot shared: the call that shared it and its result already say so, and its content is not given back.
    /// </summary>
    public static ChatMessage ToChatMessage(this Message message, IReadOnlyDictionary<Guid, FileForModel>? files = null) =>
        new(
            ToChatRole(message.Role),
            [
                .. message.Parts
                    .Where(part => message.Role is not MessageRole.Assistant || !IsFile(part))
                    .SelectMany(part => ToContents(part, files)),
            ])
        {
            MessageId = message.Id.ToString("N"),
            CreatedAt = message.CreatedAt,
        };

    /// <summary>The ids of the files people sent in the messages, each once. A file a bot shared is not given to a model.</summary>
    public static IReadOnlyCollection<Guid> FileIds(this IEnumerable<Message> messages) =>
    [
        .. messages
            .Where(message => message.Role is not MessageRole.Assistant)
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
        List<MessagePart> parts = [.. message.Contents.SelectMany(content => ToParts(content, runId))];

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

    private static bool IsFile(MessagePart part) => part is ImagePart or FilePart;

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

        foreach (var image in given.Images ?? [])
        {
            yield return new DataContent(image.Data, image.MediaType);
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

    private static IEnumerable<MessagePart> ToParts(AIContent content, Guid runId) =>
        content is TextContent text
            ? [.. text.Text is { Length: > 0 } words ? [new TextPart(words)] : Array.Empty<MessagePart>(), .. Sources(text)]
            : ToPart(content, runId) is { } part ? [part] : [];

    // A citation is the provider's word for where a reply came from. Only a web address is kept, so a source can never
    // lead anywhere but a browser.
    private static IEnumerable<SourcePart> Sources(AIContent content) =>
        (content.Annotations ?? [])
            .OfType<CitationAnnotation>()
            .Where(citation => citation.Url is { IsAbsoluteUri: true, Scheme: "http" or "https" })
            .Select(citation => new SourcePart(citation.Url!, string.IsNullOrWhiteSpace(citation.Title) ? null : citation.Title.Trim()));

    private static MessagePart? ToPart(AIContent content, Guid runId) => content switch
    {
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
