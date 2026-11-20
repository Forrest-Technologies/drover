namespace Drover.Server.Clients;

/// <summary>
/// The address the browser client was served under, <c>/native/</c>, until it took the root on 2026-11-20. A link, a
/// bookmark, or an app installed from there is sent to the same address at the root, with its query, when that
/// address is made of plain path segments; anything else is sent to the root itself. A browser that
/// installed the client there still has its service worker, which asks for its script again from time to time: it is
/// answered with one that removes that worker, what it stored, and its push subscription, and moves its open pages
/// to the root (<c>retired-native-worker.js</c>), so the person is not notified twice.
/// </summary>
internal sealed partial class RetiredNativeAddress(RequestDelegate next)
{
    #region Private Fields

    private const string Root = "/";

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

        // A local redirect refuses any address that is not a path on this server, so even a target this class built
        // wrongly could never send the browser to another host.
        return TypedResults.LocalRedirect(AtRoot(rest, context.Request.QueryString)).ExecuteAsync(context);
    }

    /// <summary>
    /// The same address at the root, built from the root and the path's segments only when every segment is plain:
    /// letters, digits, and <c>-._~</c>, never empty, never <c>.</c> or <c>..</c>. That is every address the client
    /// writes. Any other path (an empty segment, a backslash, an encoded slash, a space) is answered with the root
    /// alone, so no part of the request can make the address start with anything but one slash and a plain name.
    /// The query is kept for the root and for a plain path: a tool server's sign-in comes back with it.
    /// </summary>
    public static string AtRoot(PathString rest, QueryString query)
    {
        string[] segments = [.. (rest.Value ?? string.Empty).Split('/').Skip(1)];

        if (segments is [] or [""])
        {
            return $"{Root}{query.ToUriComponent()}";
        }

        return segments.All(IsPlainSegment) ? $"{Root}{string.Join('/', segments)}{query.ToUriComponent()}" : Root;
    }

    #endregion

    #region Private Methods

    private static bool IsPlainSegment(string segment) =>
        segment is not ("." or "..") && PlainSegment().IsMatch(segment);

    [GeneratedRegex("^[A-Za-z0-9._~-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainSegment();

    private static string ReadWorker()
    {
        using var stream = typeof(RetiredNativeAddress).Assembly.GetManifestResourceStream(WorkerResource)
            ?? throw new InvalidOperationException($"The server was built without {WorkerResource}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    #endregion
}
