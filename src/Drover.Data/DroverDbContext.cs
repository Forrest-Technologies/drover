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

    public DbSet<Bot> Bots => Set<Bot>();

    public DbSet<BotAncestor> BotAncestors => Set<BotAncestor>();

    public DbSet<BotSandboxShare> BotSandboxShares => Set<BotSandboxShare>();

    public DbSet<BotSchedule> BotSchedules => Set<BotSchedule>();

    public DbSet<BotRule> BotRules => Set<BotRule>();

    public DbSet<ToolGrant> ToolGrants => Set<ToolGrant>();

    public DbSet<ModelConnection> ModelConnections => Set<ModelConnection>();

    public DbSet<ModelCatalogEntry> ModelCatalog => Set<ModelCatalogEntry>();

    public DbSet<ToolConnection> ToolConnections => Set<ToolConnection>();

    public DbSet<ToolConnectionCredential> ToolConnectionCredentials => Set<ToolConnectionCredential>();

    public DbSet<OAuthFlow> OAuthFlows => Set<OAuthFlow>();

    public DbSet<Tool> Tools => Set<Tool>();

    public DbSet<ChatThread> Threads => Set<ChatThread>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Run> Runs => Set<Run>();

    public DbSet<RunEvent> RunEvents => Set<RunEvent>();

    public DbSet<ToolCall> ToolCalls => Set<ToolCall>();

    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();

    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();

    public DbSet<Sandbox> Sandboxes => Set<Sandbox>();

    public DbSet<StoredFile> Files => Set<StoredFile>();

    public DbSet<Secret> Secrets => Set<Secret>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

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
