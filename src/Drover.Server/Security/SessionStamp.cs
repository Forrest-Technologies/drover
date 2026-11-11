namespace Drover.Server.Security;

/// <summary>
/// The security stamp in the session cookie. Changing a person's password or stamp must end the sessions they
/// already have, so the cookie carries the stamp it was issued with and every few minutes it is checked against the
/// database, in a scope with only the user set. A changed stamp, or a user who no longer exists, signs the cookie out.
/// </summary>
public static class SessionStamp
{
    #region Private Fields

    private const string StampClaim = "drover:stamp";

    private const string CheckedClaim = "drover:stamp_checked";

    private static readonly TimeSpan checkInterval = TimeSpan.FromMinutes(5);

    #endregion

    #region Public Methods

    public static ClaimsPrincipal BuildPrincipal(
        Guid userId,
        string email,
        string displayName,
        string stamp,
        DateTimeOffset now)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, displayName),
                new Claim(StampClaim, stamp),
                new Claim(CheckedClaim, now.ToUnixTimeSeconds().ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static async Task Validate(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var principal = context.Principal;

        if (principal is null || !long.TryParse(principal.FindFirstValue(CheckedClaim), out var checkedAt))
        {
            await Reject(context);
            return;
        }

        if (now - DateTimeOffset.FromUnixTimeSeconds(checkedAt) < checkInterval)
        {
            return;
        }

        var current = await services.GetRequiredService<AccountService>()
            .GetSecurityStamp(principal.GetUserId(), context.HttpContext.RequestAborted);

        if (current is null || current != principal.FindFirstValue(StampClaim))
        {
            await Reject(context);
            return;
        }

        context.ReplacePrincipal(BuildPrincipal(
            principal.GetUserId(),
            principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            current,
            now));
        context.ShouldRenew = true;
    }

    #endregion

    #region Private Methods

    private static async Task Reject(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    #endregion
}
