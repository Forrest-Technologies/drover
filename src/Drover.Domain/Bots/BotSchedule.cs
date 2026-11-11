namespace Drover.Domain.Bots;

/// <summary>A prompt a bot is sent on a cron schedule, for its owner. The scheduler loop is Phase 1.</summary>
public sealed class BotSchedule : ISpaceOwned
{
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
}
