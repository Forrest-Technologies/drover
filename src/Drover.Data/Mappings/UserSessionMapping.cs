namespace Drover.Data.Mappings;

public sealed class UserSessionMapping : IEntityTypeConfiguration<UserSession>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.HasIndex(session => session.UserId);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_user_sessions_users_user_id");
    }

    #endregion
}
