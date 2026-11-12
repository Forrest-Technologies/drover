namespace Drover.Agents.Runtime;

/// <summary>
/// Short tenant transactions for one run, each in a dependency injection scope of its own. The run's model and tool
/// calls happen between these transactions, never inside one, and parallel tool calls never share a context.
/// </summary>
public sealed class RunDatabase(IServiceScopeFactory scopes, Guid spaceId, Guid userId)
{
    #region Properties

    public Guid SpaceId => spaceId;

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

    #endregion
}
