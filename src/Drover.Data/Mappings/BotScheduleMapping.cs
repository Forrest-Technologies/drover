namespace Drover.Data.Mappings;

public sealed class BotScheduleMapping : IEntityTypeConfiguration<BotSchedule>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<BotSchedule> builder)
    {
        builder.ToTable("bot_schedules");
        builder.HasKey(schedule => schedule.Id);
        builder.Property(schedule => schedule.Id).ValueGeneratedNever();
        builder.Property(schedule => schedule.Cron).HasMaxLength(100);
        builder.Property(schedule => schedule.TimeZone).HasMaxLength(100);
        builder.HasEnumColumn(schedule => schedule.ThreadMode);
        builder.HasEnumColumn(schedule => schedule.LastOutcome);
        builder.HasIndex(schedule => schedule.NextRunAt).HasFilter("enabled AND next_run_at IS NOT NULL");
        builder.WithinSpace<BotSchedule, Bot>(nameof(BotSchedule.BotId));
        builder.ReferencesUser(nameof(BotSchedule.OwnerUserId));
    }

    #endregion
}
