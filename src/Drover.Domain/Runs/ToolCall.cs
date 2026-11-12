namespace Drover.Domain.Runs;

/// <summary>
/// A tool call as a row, because the activity view, the approval inbox, and restart recovery all query it by
/// status. Arguments and result are stored after redaction.
/// </summary>
public sealed class ToolCall : ISpaceOwned, IAudienceScoped
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid AudienceUserId { get; set; }

    public Guid RunId { get; set; }

    public Guid ThreadId { get; set; }

    public Guid ToolId { get; set; }

    public required string ToolName { get; set; }

    public JsonElement Arguments { get; set; }

    public ToolCallStatus Status { get; set; } = ToolCallStatus.Running;

    public JsonElement? Result { get; set; }

    /// <summary>A large result is kept as a file. A plain column: files have an audience and this row's parents differ.</summary>
    public Guid? ResultFileId { get; set; }

    public bool IsError { get; set; }

    public Guid? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    public Guid? ChildRunId { get; set; }

    public Guid? SandboxId { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    #endregion
}
