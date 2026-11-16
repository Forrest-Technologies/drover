namespace Drover.Contracts.Threads;

/// <summary>
/// One stored message of a thread. <see cref="Role"/> is user, assistant, tool, or system. <see cref="Position"/> is
/// null while the message is pending: sent while the bot was working and not yet part of the conversation.
/// <see cref="Steers"/> says a pending message joins at the working run's next step, not after it.
/// <see cref="ScheduleId"/> is set on a message a schedule sent for its author, who did not type it, and
/// <see cref="ScheduleCron"/> is that schedule's time while the schedule still exists.
/// </summary>
public sealed record MessageResponse(
    Guid Id,
    Guid ThreadId,
    int? Position,
    string Role,
    Guid? RunId,
    IReadOnlyList<MessagePartResponse> Parts,
    DateTimeOffset CreatedAt,
    bool Steers = false,
    bool Withheld = false,
    Guid? ScheduleId = null,
    string? ScheduleCron = null);

/// <summary>
/// One part of a message. <see cref="Kind"/> names it (text, reasoning, tool_call, tool_result, image, file) and
/// says which of the other members are set. <see cref="Arguments"/> and <see cref="Result"/> are JSON text, already
/// redacted: a tool defines their shape, so the contract carries them as written.
/// </summary>
public sealed record MessagePartResponse(string Kind)
{
    #region Properties

    public string? Text { get; init; }

    public Guid? ToolCallId { get; init; }

    public string? ToolName { get; init; }

    public string? Arguments { get; init; }

    public string? Result { get; init; }

    public bool IsError { get; init; }

    public Guid? FileId { get; init; }

    public string? MediaType { get; init; }

    /// <summary>An image or file part's file: its name, kind, size, and how it is shown.</summary>
    public FileResponse? File { get; init; }

    #endregion
}
