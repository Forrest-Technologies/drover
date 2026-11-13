namespace Drover.Contracts.Runs;

/// <summary>
/// A hint for the signed-in person that something of theirs changed: fetch it again. <see cref="Kind"/> is one of
/// <see cref="UserSignalKinds"/>. Signals are not stored and are never replayed.
/// </summary>
public sealed record UserSignal(string Kind, Guid? ThreadId, Guid? RunId);

public static class UserSignalKinds
{
    #region Kinds

    /// <summary>A message was added to a thread, or its run changed state.</summary>
    public const string ThreadChanged = "thread.changed";

    public const string RunFinished = "run.finished";

    public const string ApprovalWaiting = "approval.waiting";

    /// <summary>Made by the client after its connection dropped: signals may have been missed, so fetch lists again.</summary>
    public const string Reconnected = "reconnected";

    #endregion
}
