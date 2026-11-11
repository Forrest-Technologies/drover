namespace Drover.Data.Tests;

/// <summary>One dependency injection scope with its tenant set and its tenant transaction open.</summary>
public sealed class TenantSession : IAsyncDisposable
{
    #region Private Fields

    private readonly AsyncServiceScope scope;

    private TenantWork? work;

    #endregion

    #region Constructors

    private TenantSession(AsyncServiceScope scope)
    {
        this.scope = scope;
    }

    #endregion

    #region Properties

    public DroverDbContext Db => scope.ServiceProvider.GetRequiredService<DroverDbContext>();

    public TenantContext Tenant => scope.ServiceProvider.GetRequiredService<TenantContext>();

    public TenantScope Scope => scope.ServiceProvider.GetRequiredService<TenantScope>();

    #endregion

    #region Public Methods

    /// <summary>Sets the tenant, as the web request or the job worker would, and opens the transaction.</summary>
    public static async Task<TenantSession> Open(IServiceProvider provider, Guid? spaceId, Guid? userId)
    {
        var session = Create(provider);

        if (userId is { } user)
        {
            session.Tenant.SetUser(user);
        }

        if (spaceId is { } space)
        {
            session.Tenant.SetSpace(space);
        }

        session.work = await session.Scope.Begin();
        return session;
    }

    /// <summary>A scope with nothing set and no transaction open.</summary>
    public static TenantSession Create(IServiceProvider provider) => new(provider.CreateAsyncScope());

    public Task Commit() => work!.Commit();

    public async ValueTask DisposeAsync()
    {
        if (work is not null)
        {
            await work.DisposeAsync();
        }

        await scope.DisposeAsync();
    }

    public async Task<int> BackendPid() =>
        await Db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync();

    #endregion
}
