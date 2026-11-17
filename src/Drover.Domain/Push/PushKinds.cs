namespace Drover.Domain.Push;

/// <summary>Why a device is woken. These strings are the whole of what a push service is told.</summary>
public static class PushKinds
{
    #region Kinds

    public const string DeviceTest = "device.test";

    public const string RunFinished = "run.finished";

    public const string RunFailed = "run.failed";

    public const string ApprovalWaiting = "approval.waiting";

    #endregion
}
