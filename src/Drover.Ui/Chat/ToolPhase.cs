namespace Drover.Ui.Chat;

/// <summary>
/// How far a tool call got, as a person reads it. Starting, Approved, and Running are live: the bot is working on
/// the call. The rest have ended, except WaitingForYou, which waits for a person's answer.
/// </summary>
public enum ToolPhase
{
    Starting,
    WaitingForYou,
    Approved,
    Running,
    Done,
    Failed,
    Denied,
    NotAllowed,
    Interrupted,
    Stopped,
}
