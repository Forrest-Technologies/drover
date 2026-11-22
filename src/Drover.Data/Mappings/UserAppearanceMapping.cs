namespace Drover.Data.Mappings;

public sealed class UserAppearanceMapping : IEntityTypeConfiguration<UserAppearance>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<UserAppearance> builder)
    {
        builder.ToTable("user_appearance");
        builder.HasKey(appearance => appearance.UserId);
        builder.Property(appearance => appearance.UserId).ValueGeneratedNever();
        builder.Property(appearance => appearance.ThemeId).HasMaxLength(AppearanceSetting.MaxThemeIdLength);
        builder.Property(appearance => appearance.Mode).HasMaxLength(8);
        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<UserAppearance>(appearance => appearance.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_appearance_users_user_id");
    }

    #endregion
}
