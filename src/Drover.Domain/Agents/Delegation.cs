namespace Drover.Domain.Agents;

/// <summary>
/// A bot handing work to another bot, as the <c>delegate</c> tool or as a member of a team. <see cref="AgentName"/>
/// names the bot as the framework knows it. A delegation through the tool also names the parent's tool call, the
/// bot it hands the work to, and the task, which becomes the first message of the child's own thread.
/// </summary>
public sealed record DelegationRequest(Guid ParentRunId, string AgentName)
{
    #region Properties

    /// <summary>The parent's tool call, as the model named it. A delegation is idempotent on it.</summary>
    public string? ParentCallId { get; init; }

    public Guid? BotId { get; init; }

    public string Task { get; init; } = string.Empty;

    #endregion
}


/// <summary>
/// A child run the governor began, or found already begun for the same tool call. <see cref="Refusal"/> is set
/// instead when the delegation was refused: it is the tool result the parent's model is given, naming why.
/// </summary>
public sealed record ChildRun(Guid RunId, string AgentName)
{
    #region Properties

    public Guid BotId { get; init; }

    public ToolOutcome? Refusal { get; init; }

    #endregion
}


/// <summary>How a child run stopped. A waiting child holds its whole tree until a person answers it.</summary>
public enum ChildRunOutcome
{
    Completed,
    Failed,
    Cancelled,
    Waiting,
}


/// <summary>
/// What a child run came to, and its final answer when it completed. <see cref="Tainted"/> says the child read text
/// from an untrusted source, itself or through a run beneath it, so whatever it hands back is that text's too.
/// </summary>
public sealed record ChildRunReport(ChildRunOutcome Outcome, string? Answer = null)
{
    #region Properties

    public bool Tainted { get; init; }

    #endregion
}
