namespace Drover.Server.Appearance;

/// <summary>Account routes for the theme choice that follows a signed-in person across devices.</summary>
public static class AppearanceEndpoints
{
    #region Private Fields

    private const string NotASetting =
        "Not a valid theme, mode, and accent. Use a lowercase hyphenated theme id and a supported mode and accent.";

    #endregion

    #region Public Methods

    public static IEndpointRouteBuilder MapAppearanceEndpoints(this RouteGroupBuilder api)
    {
        var appearance = api.MapGroup("/account/appearance").RequireAuthorization();
        appearance.MapGet("/", Get).WithName("GetAppearance")
            .WithSummary("Gets the signed-in person's account-wide appearance choice, or 404 when none is saved.");
        appearance.MapPut("/", Set)
            .WithBodyLimit(RequestLimits.Account)
            .WithName("SetAppearance")
            .WithSummary("Saves the signed-in person's appearance choice across devices.");
        return api;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<Ok<AppearanceSetting>, NotFound>> Get(
        HttpContext context,
        AppearanceService appearance) =>
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
                [nameof(AppearanceSetting.ThemeId)] = [NotASetting],
            });

    #endregion
}
