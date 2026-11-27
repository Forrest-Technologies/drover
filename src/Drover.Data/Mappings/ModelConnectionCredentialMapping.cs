namespace Drover.Data.Mappings;

public sealed class ModelConnectionCredentialMapping : IEntityTypeConfiguration<ModelConnectionCredential>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<ModelConnectionCredential> builder)
    {
        // Always one person's own: never the space's shared audience.
        builder.ToTable(
            "model_connection_credentials",
            table => table.HasCheckConstraint(
                "ck_model_connection_credentials_one_person",
                "audience_user_id <> '00000000-0000-0000-0000-000000000000'::uuid"));
        builder.HasKey(credential => credential.Id);
        builder.Property(credential => credential.Id).ValueGeneratedNever();
        builder.Property(credential => credential.AccountLabel).HasMaxLength(320);
        builder.HasEnumColumn(credential => credential.Status);
        builder.HasIndex(credential => new { credential.SpaceId, credential.ModelConnectionId, credential.AudienceUserId }).IsUnique();
        builder.WithinSpace<ModelConnectionCredential, ModelConnection>(nameof(ModelConnectionCredential.ModelConnectionId));
    }

    #endregion
}
