namespace Drover.Server.Spaces;

/// <summary>Stored model fallback lists and the effective chain members may inspect.</summary>
public static class ModelFallbackEndpoints
{
    #region Public Methods

    public static RouteGroupBuilder MapModelFallbackEndpoints(this RouteGroupBuilder space)
    {
        space.MapGet("/model-fallbacks", GetSpaceFallbacks).WithName("GetSpaceModelFallbacks");
        space.MapPut("/model-fallbacks", UpdateSpaceFallbacks)
            .RequireSpaceRole(MembershipRole.Admin)
            .WithName("UpdateSpaceModelFallbacks");
        space.MapGet("/bots/{botId:guid}/model-chain", GetBotChain).WithName("GetBotModelChain");
        return space;
    }

    #endregion

    #region Private Methods

    private static async Task<Results<Ok<SpaceModelFallbacks>, NotFound>> GetSpaceFallbacks(
        Guid spaceId,
        HttpContext context,
        ModelFallbackService fallbacks) =>
        await fallbacks.GetSpaceFallbacks(spaceId, context.RequestAborted) is { } found
            ? TypedResults.Ok(found)
            : TypedResults.NotFound();

    private static async Task<Results<Ok<SpaceModelFallbacks>, NotFound, ProblemHttpResult>> UpdateSpaceFallbacks(
        Guid spaceId,
        SpaceModelFallbacks request,
        HttpContext context,
        ModelFallbackService fallbacks)
    {
        var change = await fallbacks.UpdateSpaceFallbacks(
            spaceId,
            request.Fallbacks ?? [],
            context.GetSpaceAccess().UserId,
            context.RequestAborted);

        if (change.Problem is { } problem)
        {
            return TypedResults.Problem(title: problem, statusCode: StatusCodes.Status400BadRequest);
        }

        return change.Fallbacks is { } saved ? TypedResults.Ok(saved) : TypedResults.NotFound();
    }

    private static async Task<Results<Ok<BotModelChain>, NotFound>> GetBotChain(
        Guid botId,
        HttpContext context,
        ModelFallbackService fallbacks) =>
        await fallbacks.GetChain(botId, context.RequestAborted) is { } chain
            ? TypedResults.Ok(chain)
            : TypedResults.NotFound();

    #endregion
}
