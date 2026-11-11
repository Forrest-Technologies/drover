namespace Drover.Data.Mappings;

public sealed class UsageRecordMapping : IEntityTypeConfiguration<UsageRecord>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<UsageRecord> builder)
    {
        builder.ToTable("usage_records");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.Property(record => record.Model).HasMaxLength(200);
        builder.Property(record => record.Currency).HasMaxLength(8);
        builder.Property(record => record.CreatedAt).HasDefaultValueSql("now()");
        builder.WithinSpace<UsageRecord, Run>(nameof(UsageRecord.RunId));
        builder.WithinSpace<UsageRecord, Run>(nameof(UsageRecord.RootRunId));
        builder.WithinSpace<UsageRecord, Bot>(nameof(UsageRecord.BotId));
        builder.WithinSpace<UsageRecord, ModelConnection>(nameof(UsageRecord.ModelConnectionId));
    }

    #endregion
}
