namespace Drover.Data.Mappings;

public sealed class PushSubscriptionMapping : IEntityTypeConfiguration<PushSubscription>
{
    #region Public Methods

    public void Configure(EntityTypeBuilder<PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions");
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Id).ValueGeneratedNever();
        builder.Property(subscription => subscription.Endpoint).HasMaxLength(2048);
        builder.Property(subscription => subscription.PublicKey).HasMaxLength(128);
        builder.Property(subscription => subscription.AuthSecret).HasMaxLength(64);

        // A shared browser can hold the same endpoint for two people, and neither may see the other's row.
        builder.HasIndex(subscription => new { subscription.UserId, subscription.Endpoint }).IsUnique();
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(subscription => subscription.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_push_subscriptions_users_user_id");
    }

    #endregion
}
