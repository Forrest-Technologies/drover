namespace Drover.Data.Mappings;

public sealed class UserMapping : IEntityTypeConfiguration<User>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Ignore(user => user.DisplayName);
        builder.HasOne(user => user.Profile).WithOne().HasForeignKey<UserProfile>(profile => profile.UserId);
        builder.Navigation(user => user.Profile).AutoInclude();
        builder.Property(user => user.TermsVersion).HasMaxLength(64);
        builder.HasIndex(user => user.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
        builder.HasIndex(user => user.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");
    }

    #endregion
}
