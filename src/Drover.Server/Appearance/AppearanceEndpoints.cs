namespace Drover.Server.Appearance;

/// <summary>
/// The theme a person keeps for every device that follows their account. It is the person's in every space, so it
/// sits outside the space routes.
/// </summary>
public static class AppearanceEndpoints
{
    #region Public Methods

    public static IEndpointRouteBuilder MapAppearanceEndpoints(this RouteGroupBuilder api)
    {
        var appearance = api.MapGroup("/account/appearance").RequireAuthorization();
        appearance.MapGet("/", Get).WithName("GetAppearance")
            .WithSummary("The theme the signed-in person keeps for every device that follows their account, or 404 when they keep none.");
        appearance.MapPut("/", Set)
            .WithBodyLimit(RequestLimits.Account)
            .WithName("SetAppearance")
            .WithSummary("Keeps a theme for every device that follows the signed-in person's account.");
        return api;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<AppearanceSetting>, NotFound>> Get(HttpContext context, AppearanceService appearance) =>
        await appearance.Get(context.User.GetUserId(), context.RequestAborted) is { } kept
            ? TypedResults.Ok(kept)
            : TypedResults.NotFound();

    private static async Task<Results<NoContent, ValidationProblem>> Set(
        AppearanceSetting setting,
        HttpContext context,
        AppearanceService appearance) =>
        await appearance.Set(context.User.GetUserId(), setting, context.RequestAborted)
            ? TypedResults.NoContent()
            : TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(AppearanceSetting.ThemeId)] = ["This is not a theme and a mode: a theme id is lowercase words joined by hyphens, and the mode is system, dark or light."],
            });

    #endregion
}
