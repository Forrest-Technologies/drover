namespace Drover.Server.Security;

/// <summary>
/// The "this browser has signed in to this account before" marker. Set on every successful sign-in, it chooses
/// which lockout bucket a wrong password counts in, and nothing else: it is not a credential, signs nobody in, and
/// is read only by sign-in. It is protected with Data Protection and holds the account it belongs to, so it cannot
/// be forged, moved to another account, or read for the user id. It lasts a year.
/// </summary>
public static class KnownBrowser
{
    #region Private Fields

    private const string Purpose = "Drover.KnownBrowser.v1";

    private static readonly TimeSpan lifetime = TimeSpan.FromDays(365);

    #endregion

    #region Public Methods

    public static void Remember(HttpContext context, Guid userId)
    {
        var value = Protector(context).Protect(userId.ToString("N"), lifetime);
        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
            MaxAge = lifetime,
        });
    }

    /// <summary>The account this browser has signed in to before, or null when it carries no valid marker.</summary>
    public static Guid? SignedInBefore(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return Guid.TryParseExact(Protector(context).Unprotect(value), "N", out var userId) ? userId : null;
        }
        catch (CryptographicException exception)
        {
            // A changed, expired, or foreign marker is no marker: the attempt counts with everyone else's.
            context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(KnownBrowser).FullName!)
                .LogDebug(exception, "A sign-in carried a browser marker that could not be read; it counts as a new browser");
            return null;
        }
    }

    #endregion

    #region Private Methods

    private static ITimeLimitedDataProtector Protector(HttpContext context) =>
        context.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(Purpose)
            .ToTimeLimitedDataProtector();

    #endregion

    #region Constants

    public const string CookieName = "__Host-Drover.Known";

    #endregion
}
