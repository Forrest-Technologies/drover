namespace Drover.Server.Models;

/// <summary>
/// Where a provider's sign-in page sends the browser of a person whose Drover head cannot listen: this server's own
/// https address. No one is signed in here, so the 128-bit attempt id is the only secret and the provider's
/// <c>state</c> is still checked. It ends one waiting attempt, once; anything else is a 404. The page says only
/// words this server chose and echoes nothing of the request. The head reads the outcome from the attempt itself.
/// </summary>
public static class ModelSignInReturnEndpoint
{
    #region Private Fields

    private const string Done = "Signed in. You can close this tab.";

    private const string NotDone = "Sign-in did not finish. Close this tab and try again in Drover.";

    #endregion

    #region Public Methods

    public static IEndpointRouteBuilder MapModelSignInReturn(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{ModelSignIns.ServerReturnPath}/{{attemptId:guid}}", Return)
            .AllowAnonymous()
            .RequireRateLimiting(SecurityServiceExtensions.SignInRateLimit)
            .ExcludeFromDescription();
        return app;
    }

    #endregion

    #region Endpoints

    private static async Task<Results<ContentHttpResult, NotFound>> Return(Guid attemptId, HttpContext context, ModelSignIns signIns)
    {
        var outcome = await signIns.FinishFromReturn(
            attemptId,
            context.Request.QueryString.Value,
            context.User.Identity?.IsAuthenticated == true ? context.User.GetUserId() : null,
            context.RequestAborted);

        if (outcome.State is null)
        {
            return TypedResults.NotFound();
        }

        return Page(outcome.ConnectionId is not null && outcome.Status == 200);
    }

    #endregion

    #region Private Methods

    private static ContentHttpResult Page(bool signedIn) =>
        TypedResults.Content(
            $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
                <meta name="color-scheme" content="dark light" />
                <title>{(signedIn ? "Signed in" : "Sign-in did not finish")}</title>
                <link rel="icon" type="image/svg+xml" href="/icon.svg" />
                <link rel="stylesheet" href="/boot.css" />
            </head>
            <body>
                <main>
                    <p>{(signedIn ? Done : NotDone)}</p>
                </main>
            </body>
            </html>
            """,
            "text/html",
            Encoding.UTF8,
            signedIn ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);

    #endregion
}
