namespace Drover.Server.Clients;

/// <summary>
/// The address the browser client was served under, <c>/native/</c>, until it took the root on 2026-11-20. A link, a
/// bookmark, or an app installed from there is sent to the same address at the root, with its query. A browser that
/// installed the client there still has its service worker, which asks for its script again from time to time: it is
/// answered with one that removes that worker, what it stored, and its push subscription, and moves its open pages
/// to the root (<c>retired-native-worker.js</c>), so the person is not notified twice.
/// </summary>
internal sealed class RetiredNativeAddress(RequestDelegate next)
{
    #region Private Fields

    private const string BasePath = "/native";

    private const string WorkerPath = "/service-worker.js";

    private const string WorkerResource = "Drover.Server.Clients.retired-native-worker.js";

    private static readonly Lazy<string> worker = new(ReadWorker);

    #endregion

    #region Public Methods

    public Task Invoke(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(BasePath, out var rest))
        {
            return next(context);
        }

        var method = context.Request.Method;
        if (rest == WorkerPath && (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)))
        {
            return TypedResults.Text(worker.Value, "text/javascript", Encoding.UTF8).ExecuteAsync(context);
        }

        context.Response.Redirect(AtRoot(rest, context.Request.QueryString));
        return Task.CompletedTask;
    }

    /// <summary>
    /// The same address at the root. Leading slashes and backslashes are dropped, so the answer is always a path on
    /// this server and never an address another host could be read from.
    /// </summary>
    public static string AtRoot(PathString rest, QueryString query) =>
        $"{new PathString($"/{rest.Value?.TrimStart('/', '\\')}").ToUriComponent()}{query.ToUriComponent()}";

    #endregion

    #region Private Methods

    private static string ReadWorker()
    {
        using var stream = typeof(RetiredNativeAddress).Assembly.GetManifestResourceStream(WorkerResource)
            ?? throw new InvalidOperationException($"The server was built without {WorkerResource}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    #endregion
}
