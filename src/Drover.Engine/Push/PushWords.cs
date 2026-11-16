namespace Drover.Engine.Push;

/// <summary>What a woken device says, in a sentence that names the bot. Shown on a lock screen, so it never quotes the conversation.</summary>
public static class PushWords
{
    #region Public Methods

    public static bool Knows(string kind) => kind is PushKinds.RunFinished or PushKinds.RunFailed or PushKinds.ApprovalWaiting;

    public static string? Sentence(string kind, string botName) => kind switch
    {
        PushKinds.RunFinished => $"{botName} finished.",
        PushKinds.RunFailed => $"{botName} could not finish. Open the conversation to see why.",
        PushKinds.ApprovalWaiting => $"{botName} is waiting for you to approve a step.",
        _ => null,
    };

    #endregion
}
