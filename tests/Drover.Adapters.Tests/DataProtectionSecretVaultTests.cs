namespace Drover.Adapters.Tests;

/// <summary>The Data Protection vault through the vault conformance suite, over real PostgreSQL.</summary>
public sealed class DataProtectionSecretVaultTests(PostgresServer server)
    : SecretVaultConformance, IAsyncLifetime
{
    #region Private Fields

    private readonly string keyRing = Path.Combine(Path.GetTempPath(), $"drover-keys-{Guid.NewGuid():N}");

    private TestDatabase database = null!; // set first thing in InitializeAsync

    private ServiceProvider services = null!; // set first thing in InitializeAsync

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        database = await server.CreateDatabase();
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddDroverData(database.AppConnectionString);
        collection.AddDroverSecretVault(keyRing);
        services = collection.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();

        if (Directory.Exists(keyRing))
        {
            Directory.Delete(keyRing, recursive: true);
        }
    }

    #endregion

    #region Adapter Hooks

    protected override async Task<Guid> CreateSpace()
    {
        var spaceId = Guid.CreateVersion7();
        await using var session = await VaultSession.Open(services, spaceId, Guid.CreateVersion7());
        session.Db.Spaces.Add(new Space
        {
            Id = spaceId,
            Kind = SpaceKind.Shared,
            Name = "Conformance",
            Slug = $"conformance-{spaceId:N}",
        });
        await session.Commit();
        return spaceId;
    }

    protected override async Task<Guid> CreateMember(Guid spaceId)
    {
        var userId = Guid.CreateVersion7();
        await using var session = await VaultSession.Open(services, spaceId, userId);
        session.Db.Users.Add(new User { Id = userId, UserName = $"{userId:N}@example.test", DisplayName = "Member" });
        await session.Db.SaveChangesAsync();
        session.Db.Memberships.Add(new Membership { SpaceId = spaceId, UserId = userId, Role = MembershipRole.Member });
        await session.Commit();
        return userId;
    }

    protected override async Task<IVaultSession> OpenSession(Guid spaceId, Guid userId) =>
        await VaultSession.Open(services, spaceId, userId);

    #endregion
}
