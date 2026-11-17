namespace Drover.Server.Security;

public static class NativeBearer
{
    #region Private Fields

    public const string Scheme = BearerTokenDefaults.AuthenticationScheme;

    private const string ApiScheme = "DroverApi";

    private const string Prefix = "Bearer ";

    #endregion

    #region Public Methods

    public static AuthenticationBuilder AddNativeBearer(this AuthenticationBuilder authentication) =>
        authentication.AddPolicyScheme(ApiScheme, null, options =>
        {
            options.ForwardDefaultSelector = context =>
                context.Request.Path.StartsWithSegments("/api") && HasBearerHeader(context)
                    ? Scheme
                    : CookieAuthenticationDefaults.AuthenticationScheme;
        }).AddBearerToken(Scheme, options =>
        {
            options.BearerTokenExpiration = TimeSpan.FromMinutes(10);
            options.RefreshTokenExpiration = SessionService.IdleLimit;
            options.Events.OnMessageReceived = ValidateAccess;
        });

    public static void SelectForApi(AuthenticationOptions options)
    {
        options.DefaultAuthenticateScheme = ApiScheme;
        options.DefaultChallengeScheme = ApiScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultForbidScheme = ApiScheme;
    }

    public static bool HasBearerHeader(HttpContext context) =>
        context.Request.Headers.Authorization.ToString().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static async Task<bool> IsCurrent(ClaimsPrincipal principal, HttpContext context)
    {
        if (principal.GetSessionId() is not { } sessionId)
        {
            return false;
        }

        var services = context.RequestServices;
        var userId = principal.GetUserId();
        var cancellationToken = context.RequestAborted;
        var stamp = await services.GetRequiredService<AccountService>().GetSecurityStamp(userId, cancellationToken);
        return stamp is not null
            && SessionStamp.HasStamp(principal, stamp)
            && await services.GetRequiredService<SessionService>().IsOpen(userId, sessionId, cancellationToken);
    }

    #endregion

    #region Private Methods

    private static async Task ValidateAccess(MessageReceivedContext context)
    {
        if (!HasBearerHeader(context.HttpContext))
        {
            context.NoResult();
            return;
        }

        var token = context.Request.Headers.Authorization.ToString()[Prefix.Length..];
        var ticket = context.Options.BearerTokenProtector.Unprotect(token);
        var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        if (ticket?.Properties.ExpiresUtc is not { } expires || expires <= now
            || !await IsCurrent(ticket.Principal, context.HttpContext))
        {
            context.Fail("The native session has expired or ended.");
            return;
        }

        context.Principal = ticket.Principal;
        context.Properties = ticket.Properties;
        context.Success();
    }

    #endregion
}
