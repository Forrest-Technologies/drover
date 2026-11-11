namespace Drover.Domain.Runs;

/// <summary>
/// One bot working on one request: a durable job that a worker leases and executes in steps. A run row is
/// metadata only (which bot, status, timing, cost), so space admins can see activity and cost without being able
/// to read a private thread. It has no audience, and so no foreign key to a thread, message, or tool call.
/// </summary>
public sealed class Run : ISpaceOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid ThreadId { get; set; }

    public Guid BotId { get; set; }

    /// <summary>The human the run acts for. A child run inherits it unchanged from its parent.</summary>
    public Guid PrincipalUserId { get; set; }

    public RunTrigger Trigger { get; set; } = RunTrigger.Message;

    public Guid? TriggerMessageId { get; set; }

    public Guid? ParentRunId { get; set; }

    /// <summary>Equals <see cref="Id"/> for a root run.</summary>
    public Guid RootRunId { get; set; }

    public Guid? ParentToolCallId { get; set; }

    public int Depth { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Queued;

    public string? StatusReason { get; set; }

    public RunError? Error { get; set; }

    public bool Tainted { get; set; }

    public int Attempt { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public DateTimeOffset? CancelRequestedAt { get; set; }

    /// <summary>The last event number given out in this run's tree. Used only on the root row.</summary>
    public long LastEventSeq { get; set; }

    public RunUsage Usage { get; set; } = new();

    public RunUsage SubtreeUsage { get; set; } = new();

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }
}
