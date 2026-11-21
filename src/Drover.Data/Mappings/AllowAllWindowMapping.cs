namespace Drover.Data.Mappings;

public sealed class AllowAllWindowMapping : IEntityTypeConfiguration<AllowAllWindow>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<AllowAllWindow> builder)
    {
        builder.ToTable("allow_all_windows");
        builder.ToTable(
            table => table.HasCheckConstraint(
                "ck_allow_all_windows_ended",
                "(ended_at IS NULL AND ended_reason IS NULL) OR (ended_at IS NOT NULL AND ended_reason IS NOT NULL)"));
        builder.ToTable(
            table => table.HasCheckConstraint(
                "ck_allow_all_windows_kind",
                "kind IN ('minutes_30', 'hour_1', 'until_done', 'until_off')"));
        builder.ToTable(
            table => table.HasCheckConstraint(
                "ck_allow_all_windows_ends_at",
                "(kind IN ('minutes_30', 'hour_1')) = (ends_at IS NOT NULL)"));
        builder.ToTable(
            table => table.HasCheckConstraint(
                "ck_allow_all_windows_source",
                "(started_from = 'approval') = (tool_call_id IS NOT NULL)"));
        builder.HasKey(window => window.Id);
        builder.Property(window => window.Id).ValueGeneratedNever();
        builder.Property(window => window.StartedAt).HasDefaultValueSql("now()");
        builder.Property(window => window.Kind).HasConversion<AllowAllWindowKindConverter>().HasMaxLength(32);
        builder.HasEnumColumn(window => window.EndedReason);
        builder.HasEnumColumn(window => window.StartedFrom);

        // One open window per bot and person. The space leads the key, like every tenant index.
        builder.HasIndex(window => new { window.SpaceId, window.BotId, window.AudienceUserId })
            .IsUnique()
            .HasFilter("ended_at IS NULL")
            .HasDatabaseName("ux_allow_all_windows_one_open");

        builder.WithinSpace<AllowAllWindow, Bot>(nameof(AllowAllWindow.BotId));
    }

    #endregion
}
