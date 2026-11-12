namespace Drover.Data.Mappings;

public sealed class SecretMapping : IEntityTypeConfiguration<Secret>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<Secret> builder)
    {
        builder.ToTable("secrets");
        builder.HasKey(secret => secret.Id);
        builder.Property(secret => secret.Id).ValueGeneratedNever();
        builder.Property(secret => secret.CreatedAt).HasDefaultValueSql("now()");
        builder.HasEnumColumn(secret => secret.Kind);

        // The key a tool connection credential uses to inherit this secret's audience.
        builder.HasAlternateKey(secret => new { secret.SpaceId, secret.Id, secret.AudienceUserId });
        builder.ReferencesUser(nameof(Secret.CreatedByUserId));
    }

    #endregion
}
