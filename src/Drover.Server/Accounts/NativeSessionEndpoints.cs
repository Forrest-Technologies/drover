namespace Drover.Server.Accounts;

public static class NativeSessionEndpoints
{
    #region Public Methods

    public static void MapNativeSessionEndpoints(this RouteGroupBuilder api, IEndpointRouteBuilder app)
    {
        api.MapPost("/native/session", Start)
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme })
            .RequireRateLimiting(SecurityServiceExtensions.SignInRateLimit)
            .WithName("StartNativeSession");

        // The refresh credential is explicit in the request body. This endpoint never uses an ambient cookie.
        app.MapPost("/api/native/refresh", Refresh)
            .RequireRateLimiting(SecurityServiceExtensions.SignInRateLimit)
            .WithBodyLimit(RequestLimits.Account)
            .WithName("RefreshNativeSession");
    }

    #endregion

    #region Private Methods

    private static SignInHttpResult Start(ClaimsPrincipal principal) =>
        TypedResults.SignIn(principal, authenticationScheme: NativeBearer.Scheme);

    private static async Task<IResult> Refresh(
        NativeSessionRefresh request,
        HttpContext context,
        IOptionsMonitor<BearerTokenOptions> options,
        SessionService sessions,
        TimeProvider time)
    {
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return TypedResults.Unauthorized();
        }

        var ticket = options.Get(NativeBearer.Scheme).RefreshTokenProtector.Unprotect(request.RefreshToken);
        if (ticket?.Properties.ExpiresUtc is not { } expires || expires <= time.GetUtcNow()
            || !await NativeBearer.IsCurrent(ticket.Principal, context)
            || !await sessions.Renew(ticket.Principal.GetUserId(), ticket.Principal.GetSessionId()!.Value, context.RequestAborted))
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.SignIn(ticket.Principal, authenticationScheme: NativeBearer.Scheme);
    }

    #endregion
}
