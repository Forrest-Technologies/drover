namespace Drover.Server.Security;

/// <summary>
/// Refuses a state-changing request that does not carry the antiforgery token. A client reads the token from the
/// <c>XSRF-TOKEN</c> cookie, which <see cref="AntiforgeryTokens.Issue"/> sets, and sends it in <c>X-XSRF-TOKEN</c>.
/// </summary>
public sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    #region Interface Implementations

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)
            || HttpMethods.IsOptions(http.Request.Method))
        {
            return await next(context);
        }

        if (!await antiforgery.IsRequestValidAsync(http))
        {
            return TypedResults.Problem(
                title: "The antiforgery token is missing or invalid.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }

    #endregion
}

public static class AntiforgeryTokens
{
    #region Public Methods

    /// <summary>Stores the token pair for the current user and hands the request token to the client in a readable cookie.</summary>
    public static void Issue(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(
            SecurityServiceExtensions.AntiforgeryCookie,
            tokens.RequestToken ?? string.Empty,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
            });
    }

    #endregion
}
