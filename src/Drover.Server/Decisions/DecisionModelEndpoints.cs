namespace Drover.Server.Decisions;

/// <summary>
/// The space's one decision model, which checks bot rules written as a sentence. Reading it, setting it, replacing
/// its key and disconnecting it run under the tenant scope filter. Testing waits on the provider, so it sits outside
/// the filter and the service opens its own short scopes around the call. Any member may read; only an owner or admin
/// may change or test. No response carries the key: the connection says <c>HasKey</c> and nothing more.
/// </summary>
public static class DecisionModelEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapDecisionModelEndpoints(this RouteGroupBuilder space)
    {
        var scoped = space.MapGroup("/decision-model").AddEndpointFilter<TenantScopeFilter>();
        scoped.MapGet("/", Read).WithName("GetDecisionModel");
        scoped.MapPut("/", Set).RequireSpaceRole(MembershipRole.Admin).WithName("SetDecisionModel");
        scoped.MapPut("/key", ReplaceKey).RequireSpaceRole(MembershipRole.Admin).WithName("ReplaceDecisionModelKey");
        scoped.MapDelete("/", Disconnect).RequireSpaceRole(MembershipRole.Admin).WithName("DisconnectDecisionModel");

        var calling = space.MapGroup("/decision-model");
        calling.MapPost("/test", Test).RequireSpaceRole(MembershipRole.Admin).WithName("TestDecisionModel");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task<Ok<DecisionModelState>> Read(HttpContext context, DecisionConnectionService decisions)
    {
        var view = await decisions.Read(context.RequestAborted);
        return TypedResults.Ok(new DecisionModelState(
            view.Connection is { } connection ? ToResponse(connection, view.Kinds) : null,
            view.JudgedRules,
            [.. view.Kinds.Select(ToKind)],
            view.JudgedRulesAllowed));
    }

    private static async Task<Results<Ok<DecisionModelResponse>, ProblemHttpResult>> Set(
        SetDecisionModelRequest request,
        HttpContext context,
        DecisionConnectionService decisions)
    {
        var change = await decisions.Set(request, context.GetSpaceAccess().UserId, context.RequestAborted);
        return change.Connection is { } connection
            ? TypedResults.Ok(ToResponse(connection, decisions.OfferedKinds()))
            : TypedResults.Problem(title: change.Problem, statusCode: StatusCodes.Status400BadRequest);
    }

    private static async Task<Results<Ok<DecisionModelResponse>, NotFound, ProblemHttpResult>> ReplaceKey(
        ReplaceDecisionKeyRequest request,
        HttpContext context,
        DecisionConnectionService decisions)
    {
        var change = await decisions.ReplaceKey(request.ApiKey, context.GetSpaceAccess().UserId, context.RequestAborted);

        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        return change.Connection is { } connection ? TypedResults.Ok(ToResponse(connection, decisions.OfferedKinds())) : TypedResults.NotFound();
    }

    private static async Task<Results<NoContent, NotFound>> Disconnect(HttpContext context, DecisionConnectionService decisions) =>
        await decisions.Disconnect(context.GetSpaceAccess().UserId, context.RequestAborted) ? TypedResults.NoContent() : TypedResults.NotFound();

    private static async Task<Results<Ok<DecisionModelTestResponse>, NotFound>> Test(HttpContext context, DecisionConnectionService decisions)
    {
        if (await decisions.Test(context.RequestAborted) is not { } test)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(new DecisionModelTestResponse(
            test.Result.Succeeded,
            test.Result.Failure?.ToWireName(),
            test.Result.Message,
            ToResponse(test.Connection, decisions.OfferedKinds())));
    }

    #endregion

    #region Private Methods

    private static DecisionModelResponse ToResponse(DecisionConnection connection, IReadOnlyList<IDecisionModelProvider> kinds) =>
        new(
            connection.Kind,
            kinds.FirstOrDefault(kind => kind.Kind == connection.Kind)?.DisplayName ?? connection.Kind,
            connection.Address,
            connection.Account,
            connection.Model,
            connection.KeySecretId is not null,
            connection.Status.ToString().ToLowerInvariant(),
            connection.TestedAt,
            connection.LastError,
            connection.InputPricePerMillion);

    private static DecisionModelKindResponse ToKind(IDecisionModelProvider kind) =>
        new(kind.Kind, kind.DisplayName, kind.KeyRule.ToString().ToLowerInvariant(), kind.AddressRule.ToString().ToLowerInvariant());

    #endregion
}
