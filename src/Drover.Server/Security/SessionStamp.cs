namespace Drover.Server.Security;

/// <summary>
/// The session and the security stamp in the session cookie. The cookie names its row in <c>user_sessions</c>, and
/// every request checks the row is still there, so signing out, changing the password, or a reset ends the session on
/// the server, copied cookies included. The cookie also carries the stamp it was issued with, and every few minutes
/// that is checked against the database too. Both checks run in a scope with only the user set. A missing session
/// row, a changed stamp, or a user who no longer exists signs the cookie out.
/// </summary>
public static class SessionStamp
{
    #region Private Fields

    private const string StampClaim = "drover:stamp";

    private const string CheckedClaim = "drover:stamp_checked";

    private const string SessionClaim = "drover:session";

    private static readonly TimeSpan checkInterval = TimeSpan.FromMinutes(5);

    #endregion

    #region Public Methods

    public static ClaimsPrincipal BuildPrincipal(
        Guid userId,
        string email,
        string displayName,
        string stamp,
        Guid sessionId,
        DateTimeOffset now)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, displayName),
                new Claim(StampClaim, stamp),
                new Claim(SessionClaim, sessionId.ToString()),
                new Claim(CheckedClaim, now.ToUnixTimeSeconds().ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>The session the signed-in person's cookie names, or null when it names none.</summary>
    public static Guid? GetSessionId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SessionClaim), out var sessionId) ? sessionId : null;

    public static async Task Validate(CookieValidatePrincipalContext context)
    {
        var services = context.HttpContext.RequestServices;
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var principal = context.Principal;
        var cancellationToken = context.HttpContext.RequestAborted;

        if (principal?.GetSessionId() is not { } sessionId
            || !long.TryParse(principal.FindFirstValue(CheckedClaim), out var checkedAt))
        {
            await Reject(context);
            return;
        }

        var sessions = services.GetRequiredService<SessionService>();
        var userId = principal.GetUserId();

        if (now - DateTimeOffset.FromUnixTimeSeconds(checkedAt) < checkInterval)
        {
            if (!await sessions.IsOpen(userId, sessionId, cancellationToken))
            {
                await Reject(context);
            }

            return;
        }

        var current = await services.GetRequiredService<AccountService>().GetSecurityStamp(userId, cancellationToken);

        if (current is null || current != principal.FindFirstValue(StampClaim)
            || !await sessions.Renew(userId, sessionId, cancellationToken))
        {
            await Reject(context);
            return;
        }

        context.ReplacePrincipal(BuildPrincipal(
            userId,
            principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            current,
            sessionId,
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
