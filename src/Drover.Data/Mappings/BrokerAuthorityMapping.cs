namespace Drover.Data.Mappings;

public sealed class BrokerAuthorityMapping : IEntityTypeConfiguration<BrokerAuthority>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<BrokerAuthority> builder)
    {
        builder.ToTable("broker_authorities");
        builder.HasKey(authority => authority.Id);
        builder.Property(authority => authority.Id).ValueGeneratedNever();
        builder.Property(authority => authority.Certificate).HasMaxLength(8 * 1024);
        builder.Property(authority => authority.CreatedAt).HasDefaultValueSql("now()");
        builder.HasIndex(authority => authority.SecretId).IsUnique();
        builder.HasIndex(authority => new { authority.SpaceId, authority.NotAfter });
    }

    #endregion
}
