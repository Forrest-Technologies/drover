namespace Drover.Data.Mappings;

public sealed class DecisionConnectionMapping : IEntityTypeConfiguration<DecisionConnection>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<DecisionConnection> builder)
    {
        builder.ToTable("decision_connections");
        builder.HasKey(connection => connection.Id);
        builder.Property(connection => connection.Id).ValueGeneratedNever();
        builder.HasIndex(connection => connection.SpaceId).IsUnique();
        builder.Property(connection => connection.Kind).HasMaxLength(64);
        builder.Property(connection => connection.Address).HasMaxLength(2048);
        builder.Property(connection => connection.Account).HasMaxLength(200);
        builder.Property(connection => connection.Model).HasMaxLength(200);
        builder.Property(connection => connection.LastError).HasMaxLength(1000);
        builder.Property(connection => connection.InputPricePerMillion).HasPrecision(18, 6);
        builder.HasEnumColumn(connection => connection.Status);
    }

    #endregion
}
