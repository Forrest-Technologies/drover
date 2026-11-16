namespace Drover.Domain.Bots;

/// <summary>
/// A prompt a bot is sent on a cron schedule, for its owner. The scheduler sends it when <see cref="NextRunAt"/> has
/// passed, as an ordinary message that starts an ordinary run, and records what happened in the <c>Last</c> members.
/// </summary>
public sealed class BotSchedule : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid BotId { get; set; }

    public Guid OwnerUserId { get; set; }

    public required string Cron { get; set; }

    public required string TimeZone { get; set; }

    public required string Prompt { get; set; }

    public ScheduleThreadMode ThreadMode { get; set; } = ScheduleThreadMode.NewThread;

    public bool Enabled { get; set; } = true;

    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>The run the schedule last started. Null when it has started none, or the last time it was due started none.</summary>
    public Guid? LastRunId { get; set; }

    /// <summary>When the schedule was last due and handled, whether or not it started a run.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    public ScheduleOutcome? LastOutcome { get; set; }

    #endregion
}
