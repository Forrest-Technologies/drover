namespace Drover.Data.Mappings;

public sealed class ToolConnectionMapping : IEntityTypeConfiguration<ToolConnection>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ToolConnection> builder)
    {
        builder.ToTable("tool_connections");
        builder.HasKey(connection => connection.Id);
        builder.Property(connection => connection.Id).ValueGeneratedNever();
        builder.Property(connection => connection.Name).HasMaxLength(200);
        builder.Property(connection => connection.Slug).HasMaxLength(100);
        builder.Property(connection => connection.Url).HasMaxLength(2048);
        builder.Property(connection => connection.NegotiatedProtocol).HasMaxLength(64);
        builder.Property(connection => connection.Oauth).HasJsonRecord(defaultSql: null);
        builder.HasEnumColumn(connection => connection.Transport);
        builder.HasEnumColumn(connection => connection.AuthKind);
        builder.HasEnumColumn(connection => connection.CredentialScope);
        builder.HasEnumColumn(connection => connection.Trust);
        builder.HasEnumColumn(connection => connection.Status);
        builder.HasEnumColumn(connection => connection.ProtocolMode);
        builder.HasIndex(connection => new { connection.SpaceId, connection.Slug }).IsUnique();
    }

    #endregion
}
