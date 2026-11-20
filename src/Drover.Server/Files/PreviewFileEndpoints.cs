namespace Drover.Server.Files;

/// <summary>The unauthenticated, one-grant endpoint on the isolated preview origin.</summary>
public static class PreviewFileEndpoints
{
    #region Private Fields

    private static readonly string previewPolicy = string.Join(' ',
    [
        "default-src 'none';", "script-src 'none';", "connect-src 'none';", "frame-src 'none';", "object-src 'none';",
        "base-uri 'none';", "form-action 'none';", "img-src data:;", "font-src data:;", "style-src 'unsafe-inline';",
        "media-src 'none';", "sandbox",
    ]);

    #endregion

    #region Public Methods

    public static async Task<bool> TryServe(HttpContext context)
    {
        var origin = context.RequestServices.GetService<PreviewOrigin>();
        var previewPath = context.Request.Path == "/preview/file";
        if (origin is null)
        {
            if (context.Request.Path.StartsWithSegments("/preview"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return true;
            }

            return false;
        }

        if (!origin.IsPreviewHost(context.Request))
        {
            if (context.Request.Path.StartsWithSegments("/preview"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return true;
            }

            return false;
        }

        if (!origin.IsPreview(context.Request) || !previewPath || !HttpMethods.IsGet(context.Request.Method)
            || context.Request.Headers.ContainsKey(HeaderNames.Cookie)
            || context.Request.Query["grant"] is not { Count: 1 } values
            || await context.RequestServices.GetRequiredService<PreviewFileService>()
                .Take(values[0]!, context.RequestAborted) is not { } html)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return true;
        }

        var headers = context.Response.Headers;
        var appOrigin = origin.App.GetLeftPart(UriPartial.Authority);
        headers.ContentSecurityPolicy = $"{previewPolicy}; frame-ancestors {appOrigin}";
        headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Content-Type-Options"] = "nosniff";
        headers.CacheControl = "no-store";
        if (context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=15552000";
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = Encoding.UTF8.GetByteCount(html);
        await context.Response.WriteAsync(html, context.RequestAborted);
        return true;
    }

    #endregion
}
