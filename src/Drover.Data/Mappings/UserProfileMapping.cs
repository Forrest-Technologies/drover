namespace Drover.Data.Mappings;

public sealed class UserProfileMapping : IEntityTypeConfiguration<UserProfile>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");
        builder.HasKey(profile => profile.UserId);
        builder.Property(profile => profile.UserId).ValueGeneratedNever();
        builder.Property(profile => profile.DisplayName).HasMaxLength(200);
    }

    #endregion
}
