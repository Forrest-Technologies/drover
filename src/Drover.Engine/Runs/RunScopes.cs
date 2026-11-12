namespace Drover.Engine.Runs;

/// <summary>
/// Opens short tenant transactions for work that runs outside a request: each one in a dependency injection scope of
/// its own, begun and committed around a single piece of work. A run's model and tool calls happen between these
/// transactions, never inside one.
/// </summary>
public sealed class RunScopes(IServiceScopeFactory scopes)
{
    #region Public Methods

    /// <summary>The scopes of one run: its space, and the person it acts for.</summary>
    public RunTenant For(Guid spaceId, Guid userId) => new(scopes, spaceId, userId);

    /// <summary>
    /// A transaction with no space and no user set, for the functions in schema <c>sys</c> that work across spaces.
    /// Row-level security shows such a transaction no tenant rows at all.
    /// </summary>
    public async Task<T> WithoutTenant<T>(Func<IServiceProvider, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(cancellationToken);
        var result = await work(scope.ServiceProvider);
        await transaction.Commit(cancellationToken);
        return result;
    }

    #endregion
}


/// <summary>Short transactions as one person in one space.</summary>
public sealed class RunTenant(IServiceScopeFactory scopes, Guid spaceId, Guid userId)
{
    #region Properties

    public Guid SpaceId => spaceId;

    public Guid UserId => userId;

    #endregion

    #region Public Methods

    public async Task<T> Transact<T>(Func<IServiceProvider, Task<T>> work, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(cancellationToken);
        var result = await work(scope.ServiceProvider);
        await transaction.Commit(cancellationToken);
        return result;
    }

    public Task Transact(Func<IServiceProvider, Task> work, CancellationToken cancellationToken) =>
        Transact(
            async services =>
            {
                await work(services);
                return true;
            },
            cancellationToken);

    /// <summary>A scope with the tenant set and no transaction, for a service that opens its own.</summary>
    public async Task InScope(Func<IServiceProvider, Task> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await work(scope.ServiceProvider);
    }

    #endregion
}
