namespace Drover.Contracts.Threads;

/// <summary>
/// One stored message of a thread. <see cref="Role"/> is user, assistant, tool, or system. <see cref="Position"/> is
/// null while the message is pending: sent while the bot was working and not yet part of the conversation.
/// <see cref="Steers"/> says a pending message joins at the working run's next step, not after it.
/// <see cref="ScheduleId"/> is set on a message a schedule sent for its author, who did not type it, and
/// <see cref="ScheduleCron"/> is that schedule's time while the schedule still exists.
/// <see cref="RunPrincipalUserId"/> is the person the run that wrote the message acts for, when a run wrote it.
/// <see cref="Model"/> is the model that wrote a bot's reply, when the server kept it.
/// <see cref="Versions"/> is set when the message's place in the conversation has more than one version: a person
/// edited their message, or asked for another reply. It holds the id of every version, oldest first, this one among
/// them. The conversation shows one version of each place; showing another is asked of the server.
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
    string? ScheduleCron = null,
    Guid? RunPrincipalUserId = null,
    string? Model = null,
    IReadOnlyList<Guid>? Versions = null);

/// <summary>
/// One part of a message. <see cref="Kind"/> names it (text, reasoning, source, tool_call, tool_result, image, file,
/// answer) and says which of the other members are set. A source part is a source the reply cites:
/// <see cref="Url"/> is its web address and <see cref="Text"/> its title, when it has one. Both came from outside
/// Drover and are untrusted text. <see cref="Arguments"/> and <see cref="Result"/> are JSON text, already redacted:
/// a tool defines their shape, so the contract carries them as written.
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

    /// <summary>A source part's address: always http or https.</summary>
    public string? Url { get; init; }

    /// <summary>
    /// An answer part's answer: indexes and typed words, whose words the server writes from the stored card.
    /// </summary>
    public CardAnswer? Answer { get; init; }

    #endregion
}
