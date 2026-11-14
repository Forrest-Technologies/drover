namespace Drover.Server.Security;

/// <summary>
/// The headers every response carries. The content security policy allows no inline script and no remote origin;
/// <c>wasm-unsafe-eval</c> is there because Blazor WebAssembly requires it.
/// </summary>
public static class SecurityHeaders
{
    #region Private Fields

    // 180 days, no subdomains: the setting covers this name only.
    private const string StrictTransportSecurity = "max-age=15552000";

    private const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self' 'wasm-unsafe-eval'; "
        + "style-src 'self'; "
        + "img-src 'self' data:; "
        + "font-src 'self'; "
        + "connect-src 'self'; "
        + "object-src 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'";

    #endregion

    #region Public Methods

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers["Content-Security-Policy"] = ContentSecurityPolicy;
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                headers["X-Frame-Options"] = "DENY";

                // API answers are about one person and change with every action: no cache keeps them. A response
                // that chose its own policy, such as the event stream, keeps it.
                if (context.Request.Path.StartsWithSegments("/api") && !headers.ContainsKey("Cache-Control"))
                {
                    headers.CacheControl = "no-store";
                }

                // Only over HTTPS: behind a trusted proxy the scheme is the one the person used. Plain HTTP on this
                // machine never gets it, so http://localhost is not pinned to HTTPS.
                if (context.Request.IsHttps)
                {
                    headers["Strict-Transport-Security"] = StrictTransportSecurity;
                }

                return Task.CompletedTask;
            });
            await next();
        });

    #endregion
}
