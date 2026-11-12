namespace Drover.Data.Mappings;

public sealed class ToolConnectionCredentialMapping : IEntityTypeConfiguration<ToolConnectionCredential>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ToolConnectionCredential> builder)
    {
        builder.ToTable("tool_connection_credentials");
        builder.HasKey(credential => credential.Id);
        builder.Property(credential => credential.Id).ValueGeneratedNever();
        builder.HasEnumColumn(credential => credential.Status);
        builder.WithinSpace<ToolConnectionCredential, ToolConnection>(nameof(ToolConnectionCredential.ToolConnectionId));
        builder.WithinSpace<ToolConnectionCredential, Secret>(nameof(ToolConnectionCredential.SecretId), audience: true);
    }

    #endregion
}
