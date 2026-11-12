namespace Drover.Server.Security;

/// <summary>
/// The headers every response carries. The content security policy allows no inline script and no remote origin;
/// <c>wasm-unsafe-eval</c> is there because Blazor WebAssembly requires it.
/// </summary>
public static class SecurityHeaders
{
    #region Private Fields

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
                return Task.CompletedTask;
            });
            await next();
        });

    #endregion
}
