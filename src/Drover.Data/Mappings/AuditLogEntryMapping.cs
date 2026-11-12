namespace Drover.Data.Mappings;

public sealed class AuditLogEntryMapping : IEntityTypeConfiguration<AuditLogEntry>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Action).HasMaxLength(200);
        builder.Property(entry => entry.TargetKind).HasMaxLength(64);
        builder.Property(entry => entry.Data).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(entry => entry.CreatedAt).HasDefaultValueSql("now()");
        builder.HasEnumColumn(entry => entry.ActorKind);
        builder.HasIndex(entry => new { entry.SpaceId, entry.CreatedAt });
    }

    #endregion
}
