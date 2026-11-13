namespace Drover.Server.Models;

/// <summary>
/// Model connections of a space. Creating one, replacing its key, and the reads run under the tenant scope filter.
/// Testing and refreshing wait on the provider, so they sit outside the filter and the service opens its own short
/// scopes around the call. No response carries a key: a connection says <c>HasKey</c> and nothing more.
/// </summary>
public static class ModelConnectionEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapModelConnectionEndpoints(this RouteGroupBuilder space)
    {
        var scoped = space.MapGroup("/model-connections").AddEndpointFilter<TenantScopeFilter>();
        scoped.MapGet("/", ListConnections).WithName("ListModelConnections");
        scoped.MapPost("/", CreateConnection).RequireSpaceRole(MembershipRole.Admin).WithName("CreateModelConnection");
        scoped.MapPut("/{connectionId:guid}/key", ReplaceKey).RequireSpaceRole(MembershipRole.Admin).WithName("ReplaceModelKey");
        scoped.MapGet("/{connectionId:guid}/models", ListModels).WithName("ListModels");

        var calling = space.MapGroup("/model-connections");
        calling.MapPost("/{connectionId:guid}/test", TestConnection).RequireSpaceRole(MembershipRole.Admin).WithName("TestModelConnection");
        calling.MapPost("/{connectionId:guid}/models/refresh", RefreshModels).RequireSpaceRole(MembershipRole.Admin).WithName("RefreshModels");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Ok<List<ModelConnectionSummary>>> ListConnections(HttpContext context, ModelConnectionService models) =>
        TypedResults.Ok((await models.ListConnections(context.RequestAborted)).Select(connection => connection.ToSummary()).ToList());

    private static async Task<Results<Created<ModelConnectionSummary>, ProblemHttpResult>> CreateConnection(
        CreateModelConnectionRequest request,
        HttpContext context,
        ModelConnectionService models)
    {
        var change = await models.Create(
            request.Kind ?? string.Empty,
            request.Name ?? string.Empty,
            request.BaseUrl,
            request.ApiKey,
            request.DefaultModel,
            context.RequestAborted);

        return change.Connection is { } connection
            ? TypedResults.Created($"/api/spaces/{connection.SpaceId}/model-connections", connection.ToSummary())
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<ModelConnectionSummary>, NotFound, ProblemHttpResult>> ReplaceKey(
        Guid connectionId,
        UpdateModelKeyRequest request,
        HttpContext context,
        ModelConnectionService models)
    {
        var change = await models.ReplaceKey(connectionId, request.ApiKey ?? string.Empty, context.RequestAborted);

        if (change.Connection is { } connection)
        {
            return TypedResults.Ok(connection.ToSummary());
        }

        return change.Problem is null
            ? TypedResults.NotFound()
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<List<ModelCatalogResponse>>, NotFound>> ListModels(
        Guid connectionId,
        HttpContext context,
        ModelConnectionService models)
    {
        var catalog = await models.ListModels(connectionId, context.RequestAborted);
        return catalog is null ? TypedResults.NotFound() : TypedResults.Ok(catalog.Select(entry => entry.ToResponse()).ToList());
    }

    private static async Task<Results<Ok<ConnectionTestResponse>, NotFound>> TestConnection(
        Guid connectionId,
        HttpContext context,
        ModelConnectionService models)
    {
        var check = await models.Test(connectionId, context.RequestAborted);

        return check is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ConnectionTestResponse(
                check.Result?.Succeeded ?? false,
                check.Message,
                check.Failure?.ToWireName(),
                check.Connection.ToSummary()));
    }

    private static async Task<Results<Ok<ModelRefreshResponse>, NotFound>> RefreshModels(
        Guid connectionId,
        HttpContext context,
        ModelConnectionService models)
    {
        var check = await models.RefreshModels(connectionId, context.RequestAborted);

        return check is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(new ModelRefreshResponse(
                check.Failure is null,
                check.Message,
                check.Failure?.ToWireName(),
                [.. check.Catalog.Select(entry => entry.ToResponse())]));
    }

    #endregion
}
