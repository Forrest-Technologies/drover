namespace Drover.Ui.Session;

/// <summary>Decides which live signals become system notifications and where a selected notification opens.</summary>
public static class LiveNotificationDecision
{
    #region Public Methods

    /// <summary>The safe wake-text kind for a signal that calls for a notification.</summary>
    public static string? Kind(UserSignal signal) => signal is
    {
        Kind: UserSignalKinds.RunFinished or UserSignalKinds.ApprovalWaiting,
        ThreadId: not null,
    }
        ? signal.Kind
        : null;

    /// <summary>A muted bot does not notify. The foreground conversation stays quiet while it is being used.</summary>
    public static bool ShouldShow(PushWakeText wake, bool notifies, string currentAddress, bool appActive) =>
        notifies && (!appActive || !IsOpen(wake, currentAddress));

    /// <summary>The conversation a selected notification opens.</summary>
    public static string Address(PushWakeText wake) =>
        wake.IsMain ? Addresses.Talk(wake.BotId) : Addresses.SideChat(wake.BotId, wake.ThreadId);

    #endregion

    #region Private Methods

    private static bool IsOpen(PushWakeText wake, string currentAddress)
    {
        var path = AddressParts.Split(currentAddress).Path;
        if (path == Addresses.Conversation(wake.ThreadId))
        {
            return true;
        }

        var address = Address(wake);
        return wake.IsMain
            ? path == address || path.StartsWith($"{address}/", StringComparison.Ordinal)
            : path == address;
    }

    #endregion
}
