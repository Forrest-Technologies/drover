namespace Drover.Server.Spaces;

/// <summary>
/// Opens the tenant transaction for the endpoint, commits it when the endpoint succeeds, and rolls it back
/// otherwise. The transaction ends before the response is written. It is held for the whole endpoint, so an
/// endpoint that waits on the network (a model call, a tool call) must not sit under this filter.
/// </summary>
public sealed class TenantScopeFilter(TenantScope scope) : IEndpointFilter
{
    #region Interface Implementations

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var cancellationToken = context.HttpContext.RequestAborted;
        await using var work = await scope.Begin(cancellationToken);
        var result = await next(context);

        // A Results<T1, T2> hides the result it holds, so look inside before reading the status code.
        var outcome = result is INestedHttpResult nested ? nested.Result : result;

        if (outcome is not IStatusCodeHttpResult { StatusCode: >= 400 })
        {
            await work.Commit(cancellationToken);
        }

        return result;
    }

    #endregion
}
