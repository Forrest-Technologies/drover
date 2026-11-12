namespace Drover.Contracts.Runs;

/// <summary>The states of a run. Transitions are in docs/architecture/03-run-lifecycle.md.</summary>
public enum RunStatus
{
    Queued,
    Running,
    Waiting,
    Completed,
    Failed,
    Cancelled,
}
