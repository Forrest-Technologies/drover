namespace Drover.Server.Tests.Support;

/// <summary>
/// One server over one database for one test. Setup works only once per database, and sign-in changes lockout
/// counts, so tests do not share a server.
/// </summary>
public abstract class ServerTestBase(PostgresServer postgres) : IAsyncLifetime
{
    #region Private Fields

    private readonly string keyRing = Path.Combine(Path.GetTempPath(), $"drover-keys-{Guid.NewGuid():N}");

    private TestDatabase database = null!; // set first thing in InitializeAsync

    private DroverFactory factory = null!; // set first thing in InitializeAsync

    #endregion

    #region Properties

    protected LogCapture Logs { get; } = new();

    protected AdjustableTimeProvider Time { get; } = new();

    protected HashCounter Hashes { get; } = new();

    protected IServiceProvider Services => factory.Services;

    protected string KeyRingPath => keyRing;

    protected string AppConnectionString => database.AppConnectionString;

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Configuration for the server under test. The sign-in rate limit is out of the way unless a test asks.</summary>
    protected virtual IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Drover:SignInRateLimit:PermitLimit"] = "1000",
    };

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        database = await postgres.CreateDatabase();
        factory = new DroverFactory(database, keyRing, Logs, Time, Hashes, Settings, configure: ConfigureServices);
    }

    public async ValueTask DisposeAsync()
    {
        await factory.DisposeAsync();
        await database.DisposeAsync();

        if (Directory.Exists(keyRing))
        {
            Directory.Delete(keyRing, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    #region Helpers

    /// <summary>Lets a test replace or add services in the server under test.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected ApiClient NewClient(string? remoteIp = null)
    {
        var http = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
        return new ApiClient(http) { RemoteIp = remoteIp };
    }

    /// <summary>Creates the first account through the API, with the code from the log.</summary>
    protected async Task<(ApiClient Client, SessionResponse Session, string Password)> SetUpFirstAccount(
        string email = "owner@example.test")
    {
        var client = NewClient("127.0.0.1");
        var password = NewPassword();
        var code = Services.GetRequiredService<SetupCode>().Display;
        var response = await client.Post("/api/setup", new SetupRequest(email, "Owner", password, code));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>(Token))!;
        return (client, session, password);
    }

    /// <summary>Another person with a personal space of their own, made the way the first account is.</summary>
    protected async Task<(string Email, string Password, Guid UserId, Guid SpaceId)> SeedPerson(string name)
    {
        var email = $"{name}@example.test";
        var password = NewPassword();
        var userId = Guid.CreateVersion7();
        var spaceId = Guid.CreateVersion7();
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var db = provider.GetRequiredService<DroverDbContext>();
        var created = await provider.GetRequiredService<UserManager<User>>().CreateAsync(
            new User { Id = userId, UserName = email, Email = email, DisplayName = name },
            password);
        created.Succeeded.Should().BeTrue();
        db.Spaces.Add(new Space { Id = spaceId, Kind = SpaceKind.Personal, Name = name, Slug = $"personal-{spaceId:N}" });
        db.Memberships.Add(new Membership { SpaceId = spaceId, UserId = userId, Role = MembershipRole.Owner });
        await work.Commit(Token);
        return (email, password, userId, spaceId);
    }

    /// <summary>Adds a person to an existing space with the given role, and returns their credentials.</summary>
    protected async Task<(string Email, string Password, Guid UserId)> SeedMember(Guid spaceId, string name, MembershipRole role)
    {
        var email = $"{name}@example.test";
        var password = NewPassword();
        var userId = Guid.CreateVersion7();
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var created = await provider.GetRequiredService<UserManager<User>>().CreateAsync(
            new User { Id = userId, UserName = email, Email = email, DisplayName = name },
            password);
        created.Succeeded.Should().BeTrue();
        provider.GetRequiredService<DroverDbContext>().Memberships.Add(
            new Membership { SpaceId = spaceId, UserId = userId, Role = role });
        await work.Commit(Token);
        return (email, password, userId);
    }

    /// <summary>Stores a secret the way a service would, inside a tenant scope, and returns its id.</summary>
    protected async Task<Guid> SeedSecret(Guid spaceId, Guid userId, string value)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var id = await provider.GetRequiredService<ISecretVault>().Store(SecretKind.ModelKey, value, null, Token);
        await work.Commit(Token);
        return id;
    }

    protected static string NewPassword() => $"pw-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";

    #endregion
}
