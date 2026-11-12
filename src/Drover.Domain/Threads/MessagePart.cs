namespace Drover.Domain.Threads;

/// <summary>
/// One part of a message, in Drover's own shape. No vendor message type is ever serialized into the column. The
/// <c>type</c> property names the part: text, reasoning, image, file, tool_call, tool_result.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(ReasoningPart), "reasoning")]
[JsonDerivedType(typeof(ImagePart), "image")]
[JsonDerivedType(typeof(FilePart), "file")]
[JsonDerivedType(typeof(ToolCallPart), "tool_call")]
[JsonDerivedType(typeof(ToolResultPart), "tool_result")]
public abstract record MessagePart;

public sealed record TextPart(string Text) : MessagePart;

public sealed record ReasoningPart(string Text) : MessagePart;

public sealed record ImagePart(Guid FileId, string MediaType, string? AltText = null) : MessagePart;

public sealed record FilePart(Guid FileId, string Name, string MediaType) : MessagePart;

/// <summary>
/// <see cref="ToolCallId"/> is the <c>tool_calls</c> row. <see cref="CallId"/> is the id the model gave the call; it
/// pairs the call with its result when the history is sent back to a model.
/// </summary>
public sealed record ToolCallPart(Guid ToolCallId, string CallId, string ToolName, JsonElement Arguments)
    : MessagePart;

public sealed record ToolResultPart(Guid ToolCallId, string CallId, bool IsError, JsonElement Result) : MessagePart;
