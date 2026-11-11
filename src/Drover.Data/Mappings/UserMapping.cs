namespace Drover.Data.Mappings;

public sealed class UserMapping : IEntityTypeConfiguration<User>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(user => user.DisplayName).HasMaxLength(200);
        builder.HasIndex(user => user.NormalizedEmail).HasDatabaseName("ix_users_normalized_email");
        builder.HasIndex(user => user.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");
    }

    #endregion
}
