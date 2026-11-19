namespace Drover.Domain.Threads;

/// <summary>
/// One part of a message, in Drover's own shape. No vendor message type is ever serialized into the column. The
/// <c>type</c> property names the part: text, reasoning, source, image, file, tool_call, tool_result.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextPart), "text")]
[JsonDerivedType(typeof(ReasoningPart), "reasoning")]
[JsonDerivedType(typeof(SourcePart), "source")]
[JsonDerivedType(typeof(ImagePart), "image")]
[JsonDerivedType(typeof(FilePart), "file")]
[JsonDerivedType(typeof(ToolCallPart), "tool_call")]
[JsonDerivedType(typeof(ToolResultPart), "tool_result")]
public abstract record MessagePart;

public sealed record TextPart(string Text) : MessagePart;

public sealed record ReasoningPart(string Text) : MessagePart;

/// <summary>
/// A source a reply cites: a web address and the title the model's provider gave it. Both come from outside Drover and
/// are untrusted text. Only an http or https address is kept.
/// </summary>
public sealed record SourcePart(Uri Url, string? Title = null) : MessagePart;

/// <summary>
/// A picture, by its <c>files</c> row. The name, size, and dimensions are copied from the row when the message is
/// sent, so the conversation can show the picture at its size without reading the row. <see cref="Caption"/> is the
/// line a bot wrote under a picture it shared.
/// </summary>
public sealed record ImagePart(
    Guid FileId,
    string MediaType,
    string? AltText = null,
    string? Name = null,
    long Size = 0,
    int? Width = null,
    int? Height = null,
    string? Caption = null) : MessagePart;

/// <summary>
/// Any other file, by its <c>files</c> row. A text file carries its first lines for the conversation to show.
/// <see cref="Caption"/> is the line a bot wrote under a file it shared.
/// </summary>
public sealed record FilePart(Guid FileId, string Name, string MediaType, long Size = 0, string? Preview = null, string? Caption = null)
    : MessagePart;

/// <summary>
/// <see cref="ToolCallId"/> is the <c>tool_calls</c> row. <see cref="CallId"/> is the id the model gave the call; it
/// pairs the call with its result when the history is sent back to a model.
/// </summary>
public sealed record ToolCallPart(Guid ToolCallId, string CallId, string ToolName, JsonElement Arguments)
    : MessagePart;

public sealed record ToolResultPart(Guid ToolCallId, string CallId, bool IsError, JsonElement Result) : MessagePart;
