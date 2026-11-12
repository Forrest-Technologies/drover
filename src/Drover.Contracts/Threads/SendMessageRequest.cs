namespace Drover.Contracts.Threads;

/// <summary>
/// A message for the thread's bot. <see cref="WhenBusy"/> says what happens if the bot is still working on an
/// earlier one: <c>steer</c> (the default) hands it to that run at its next step, <c>queue</c> holds it until the
/// run has ended. Either way the message is stored at once and never refused for arriving early.
/// </summary>
public sealed record SendMessageRequest(string? Text, string? WhenBusy = null);

/// <summary>The values of <see cref="SendMessageRequest.WhenBusy"/>.</summary>
public static class BusyModes
{
    #region Modes

    public const string Steer = "steer";

    public const string Queue = "queue";

    #endregion
}

/// <summary>
/// The stored message and the run that answers it. For a message sent while the bot was working, that is the run
/// already under way: the message is pending (it has no position yet) and the run's events say when it is delivered.
/// </summary>
public sealed record SendMessageResponse(MessageResponse Message, RunSummary Run);
