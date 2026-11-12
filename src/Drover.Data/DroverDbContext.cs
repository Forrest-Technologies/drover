namespace Drover.Data;

public sealed class DroverDbContext : IdentityUserContext<User, Guid, UserClaim, UserLogin, UserToken>
{
    #region Constructors

    // A traditional constructor, because a primary constructor cannot run the statement below.
    public DroverDbContext(DbContextOptions<DroverDbContext> options)
        : base(options)
    {
        // SaveChanges must never open a transaction of its own. Only a TenantScope does, so the tenant is always set.
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
    }

    #endregion

    #region Properties

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<Space> Spaces => Set<Space>();

    public DbSet<Membership> Memberships => Set<Membership>();

    #endregion

    #region Model

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(DroverDbContext).Assembly);
        builder.RenameIdentityTables();
    }

    #endregion
}
