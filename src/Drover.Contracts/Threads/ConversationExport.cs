namespace Drover.Contracts.Threads;

/// <summary>
/// A conversation as a person saves it, in the JSON form of
/// <c>GET /api/spaces/{spaceId}/threads/{threadId}/export?format=json</c>. <see cref="Version"/> is the version of
/// this shape: a reader checks it first, and a change that renames or removes a field gets a new version. Fields may
/// be added within a version.
/// <para>
/// <see cref="Messages"/> are what the person and the bot said, in the conversation's order, then the messages still
/// waiting for the bot in the order they were sent. Tool results are not messages here: each tool call is listed on
/// the bot's message that made it, by name and outcome only, because a tool's arguments and output are untrusted
/// text that may hold anything. Files are listed by name, type, and size, never by content. Nothing in it is a secret.
/// </para>
/// </summary>
public sealed record ConversationExport(
    int Version,
    Guid ThreadId,
    Guid BotId,
    string BotName,
    string? Title,
    bool IsMain,
    bool IsShared,
    DateTimeOffset ExportedAt,
    IReadOnlyList<ExportedMessage> Messages)
{
    #region Constants

    /// <summary>The version of the export this server writes.</summary>
    public const int CurrentVersion = 1;

    #endregion
}

/// <summary>
/// One message of an exported conversation. <see cref="Role"/> is <c>user</c> for a person and <c>assistant</c> for
/// the bot. <see cref="Position"/> is its place in the conversation, null while it is <see cref="Pending"/>: sent
/// while the bot was working and not taken by a run yet. <see cref="Text"/> is its text parts joined by a blank line.
/// <see cref="Withheld"/> says a rule of the bot kept it from the bot; <see cref="SentBySchedule"/> says a routine
/// sent it for the person.
/// </summary>
public sealed record ExportedMessage(
    int? Position,
    string Role,
    DateTimeOffset CreatedAt,
    string Text,
    IReadOnlyList<ExportedToolCall> ToolCalls,
    IReadOnlyList<ExportedAttachment> Attachments,
    bool Pending = false,
    bool Withheld = false,
    bool SentBySchedule = false);

/// <summary>A tool call the bot made, by the tool's name and one of <see cref="ExportedToolOutcomes"/>.</summary>
public sealed record ExportedToolCall(string Name, string Outcome);

/// <summary>A file that went with a message: its name, its media type, and its size in bytes.</summary>
public sealed record ExportedAttachment(string Name, string MediaType, long Size);

/// <summary>The values of <see cref="ExportedToolCall.Outcome"/>.</summary>
public static class ExportedToolOutcomes
{
    #region Outcomes

    public const string Succeeded = "succeeded";

    public const string Failed = "failed";

    public const string Denied = "denied";

    public const string Interrupted = "interrupted";

    public const string AwaitingApproval = "awaiting_approval";

    public const string Running = "running";

    /// <summary>The call's record is gone and no result says how it ended.</summary>
    public const string Unknown = "unknown";

    #endregion
}

/// <summary>The values of the export's <c>format</c> query.</summary>
public static class ExportFormats
{
    #region Formats

    public const string Markdown = "markdown";

    public const string Json = "json";

    #endregion
}
