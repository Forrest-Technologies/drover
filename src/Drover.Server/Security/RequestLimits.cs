namespace Drover.Server.Security;

/// <summary>
/// How large a request body may be. Every endpoint takes JSON, so the server caps bodies at
/// <see cref="Default"/>; the account endpoints, which anyone can reach without signing in, take
/// <see cref="Account"/>. A body declared larger than its endpoint allows is refused with 413 before any of it is
/// read. One sent without a declared length is cut off by the server at the same limit while it is read.
/// </summary>
public static class RequestLimits
{
    #region Public Methods

    /// <summary>
    /// A body limit for one endpoint. With <paramref name="refusal"/>, a body declared larger is refused with that
    /// sentence as a problem, given the declared length, instead of a bare 413.
    /// </summary>
    public static TBuilder WithBodyLimit<TBuilder>(this TBuilder builder, long size, Func<long, string>? refusal = null)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new BodyLimit(size, refusal));

    /// <summary>The server's own cap, for bodies sent without a declared length, and no header naming the server.</summary>
    public static IServiceCollection AddRequestLimits(this IServiceCollection services) =>
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = Default;
            options.AddServerHeader = false;
        });

    /// <summary>Runs after routing, so it knows the endpoint, and before anything reads the body.</summary>
    public static IApplicationBuilder UseRequestLimits(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
            var limit = metadata?.MaxRequestBodySize ?? Default;

            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
            {
                feature.MaxRequestBodySize = limit;
            }

            if (context.Request.ContentLength is { } declared && declared > limit)
            {
                return metadata is BodyLimit { Refusal: { } refusal }
                    ? TypedResults.Problem(title: refusal(declared), statusCode: StatusCodes.Status413PayloadTooLarge).ExecuteAsync(context)
                    : TooLarge(context);
            }

            return next(context);
        });

    #endregion

    #region Private Methods

    private static Task TooLarge(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return Task.CompletedTask;
    }

    #endregion

    #region Constants

    /// <summary>The longest chat message, every character escaped, is about 600 KB.</summary>
    public const long Default = 1024 * 1024;

    /// <summary>A sign-in, setup, registration or reset form is a few hundred bytes.</summary>
    public const long Account = 64 * 1024;

    #endregion

    #region Nested Types

    private sealed record BodyLimit(long? MaxRequestBodySize, Func<long, string>? Refusal = null) : IRequestSizeLimitMetadata;

    #endregion
}
