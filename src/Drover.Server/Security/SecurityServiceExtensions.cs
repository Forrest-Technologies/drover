namespace Drover.Server.Security;

/// <summary>How many sign-in or setup attempts one address may make in a window.</summary>
public sealed class SignInRateLimitOptions
{
    public int PermitLimit { get; set; } = 10;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

public static class SecurityServiceExtensions
{
    #region Public Methods

    /// <summary>
    /// Cookie sign-in, antiforgery for state-changing requests, and the rate limit on sign-in. The cookies are
    /// <c>HttpOnly</c>, always <c>Secure</c> whatever scheme the request came in on, and carry the <c>__Host-</c>
    /// prefix, so a browser only keeps them for a secure origin: HTTPS, or <c>http://localhost</c>.
    /// </summary>
    public static IServiceCollection AddDroverSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SignInRateLimitOptions>(configuration.GetSection("Drover:SignInRateLimit"));
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(ConfigureSessionCookie);
        services.AddAuthorization();
        services.TryAddSingleton(TimeProvider.System);
        services.AddAntiforgery(ConfigureAntiforgery);
        services.Configure<CookiePolicyOptions>(options => options.Secure = CookieSecurePolicy.Always);
        services.AddTrustedProxies(configuration);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(SignInRateLimit, PartitionByAddress);
        });
        return services;
    }

    #endregion

    #region Constants

    public const string SignInRateLimit = "sign-in";

    public const string AntiforgeryHeader = "X-XSRF-TOKEN";

    public const string AntiforgeryCookie = "XSRF-TOKEN";

    #endregion

    #region Private Methods

    private static void ConfigureSessionCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "__Host-Drover.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = SessionService.IdleLimit;

        // An API answers with a status code, never a redirect to a sign-in page.
        options.Events.OnValidatePrincipal = SessionStamp.Validate;
        options.Events.OnRedirectToLogin = context => Refuse(context.Response, StatusCodes.Status401Unauthorized);
        options.Events.OnRedirectToAccessDenied = context => Refuse(context.Response, StatusCodes.Status403Forbidden);
    }

    private static void ConfigureAntiforgery(AntiforgeryOptions options)
    {
        options.HeaderName = AntiforgeryHeader;
        options.Cookie.Name = "__Host-Drover.Antiforgery";
        options.Cookie.HttpOnly = true;

        // Not "Always": antiforgery refuses to answer a plain-HTTP request under that setting, which breaks
        // http://localhost. The cookie is still always Secure: over HTTPS this sets it, and over plain HTTP the
        // cookie policy stamps Secure on every cookie the server sends.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
    }

    private static RateLimitPartition<string> PartitionByAddress(HttpContext context)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<SignInRateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.PermitLimit,
                Window = limits.Window,
                QueueLimit = 0,
            });
    }

    private static Task Refuse(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }

    #endregion
}
