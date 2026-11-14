namespace Drover.Server.Security;

/// <summary>
/// The "this browser has signed in to this account before" marker. Set on every successful sign-in, it chooses
/// which lockout bucket a wrong password counts in, and nothing else: it is not a credential, signs nobody in, and
/// is read only by sign-in. It is protected with Data Protection and holds the account, a digest of the account's
/// security stamp, and when it runs out, so it cannot be forged, moved to another account, or read for the user
/// id; a password change or a reset makes it count as no marker; and it lasts <see cref="Lifetime"/>, checked on
/// the server's clock. A browser that signs in again gets a fresh one.
/// </summary>
public static class KnownBrowser
{
    #region Private Fields

    private const string Purpose = "Drover.KnownBrowser.v2";

    #endregion

    #region Properties

    public static TimeSpan Lifetime { get; } = TimeSpan.FromDays(30);

    #endregion

    #region Public Methods

    public static void Remember(HttpContext context, Guid userId, string securityStamp)
    {
        var expiresAt = Clock(context).GetUtcNow() + Lifetime;
        var payload = $"{userId:N}.{BrowserMarker.Digest(securityStamp)}.{expiresAt.ToUnixTimeSeconds()}";
        context.Response.Cookies.Append(CookieName, Protector(context).Protect(payload), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            MaxAge = Lifetime,
        });
    }

    /// <summary>What the browser's marker says, or null when it carries none that is readable and unexpired.</summary>
    public static BrowserMarker? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        string payload;

        try
        {
            payload = Protector(context).Unprotect(value);
        }
        catch (CryptographicException exception)
        {
            // A changed or foreign marker is no marker: the attempt counts with everyone else's.
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(KnownBrowser).FullName!)
                .LogDebug(exception, "A sign-in carried a browser marker that could not be read; it counts as a new browser");
            return null;
        }

        return payload.Split('.') is [var user, var digest, var expires]
            && Guid.TryParseExact(user, "N", out var userId)
            && long.TryParse(expires, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)
            && Clock(context).GetUtcNow() < DateTimeOffset.FromUnixTimeSeconds(expiresAt)
                ? new BrowserMarker(userId, digest)
                : null;
    }

    #endregion

    #region Private Methods

    private static IDataProtector Protector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    private static TimeProvider Clock(HttpContext context) =>
        context.RequestServices.GetRequiredService<TimeProvider>();

    #endregion

    #region Constants

    public const string CookieName = "__Host-Drover.Known";

    #endregion
}
