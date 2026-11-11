namespace Drover.Server.Spaces;

/// <summary>
/// Opens the tenant transaction for the endpoint, commits it when the endpoint succeeds, and rolls it back
/// otherwise. The transaction ends before the response is written, so it is never held across a network wait.
/// </summary>
public sealed class TenantScopeFilter(TenantScope scope) : IEndpointFilter
{
    #region Interface Implementations

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var cancellationToken = context.HttpContext.RequestAborted;
        await using var work = await scope.Begin(cancellationToken);
        var result = await next(context);

        if (result is not IStatusCodeHttpResult { StatusCode: >= 400 })
        {
            await work.Commit(cancellationToken);
        }

        return result;
    }

    #endregion
}
