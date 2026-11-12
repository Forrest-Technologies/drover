namespace Drover.Contracts.Runs;

public enum ToolCallStatus
{
    PendingApproval,
    Approved,
    Denied,
    Running,
    Completed,
    Failed,
    Interrupted,
}
