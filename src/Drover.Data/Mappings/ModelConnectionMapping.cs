namespace Drover.Data.Mappings;

public sealed class ModelConnectionMapping : IEntityTypeConfiguration<ModelConnection>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ModelConnection> builder)
    {
        builder.ToTable("model_connections");
        builder.HasKey(connection => connection.Id);
        builder.Property(connection => connection.Id).ValueGeneratedNever();
        builder.Property(connection => connection.Name).HasMaxLength(200);
        builder.Property(connection => connection.Kind).HasMaxLength(64);
        builder.Property(connection => connection.BaseUrl).HasMaxLength(2048);
        builder.Property(connection => connection.DefaultModel).HasMaxLength(200);
        builder.Property(connection => connection.Balance).HasJsonRecord(defaultSql: null);
        builder.HasEnumColumn(connection => connection.AuthKind);
        builder.HasEnumColumn(connection => connection.CredentialScope);
        builder.HasEnumColumn(connection => connection.Status);
    }

    #endregion
}
