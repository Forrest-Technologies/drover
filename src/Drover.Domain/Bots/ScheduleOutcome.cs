namespace Drover.Domain.Bots;

/// <summary>What happened the last time a schedule was due.</summary>
public enum ScheduleOutcome
{
    /// <summary>A run was queued for the schedule's message. The run's own status says how it went.</summary>
    Started,

    /// <summary>The conversation was busy, so the message waits for the reply under way and starts the next run.</summary>
    WaitingForReply,

    /// <summary>The bot was paused, so nothing was sent. The schedule stays on.</summary>
    BotPaused,

    /// <summary>The bot was removed, so nothing was sent. The schedule stays on.</summary>
    BotRemoved,

    /// <summary>The schedule's owner is no longer a member of the space, so nothing was sent and the schedule was turned off.</summary>
    OwnerLeft,
}
