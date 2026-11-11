namespace Drover.Data.Mappings;

public sealed class MembershipMapping : IEntityTypeConfiguration<Membership>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.Id).ValueGeneratedNever();
        builder.Property(membership => membership.Role)
            .HasConversion<LowercaseEnumConverter<MembershipRole>>()
            .HasMaxLength(16);
        builder.HasIndex(membership => new { membership.SpaceId, membership.UserId }).IsUnique();
        builder.HasIndex(membership => membership.UserId);
        builder.HasOne<Space>().WithMany().HasForeignKey(membership => membership.SpaceId);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .HasConstraintName("fk_memberships_users_user_id");
        builder.ToTable(table => table.HasCheckConstraint("ck_memberships_role", "role IN ('owner', 'admin', 'member')"));
    }

    #endregion
}
