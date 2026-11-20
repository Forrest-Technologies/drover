namespace Drover.Server.Clients;

/// <summary>
/// The address the browser client was served under, <c>/native/</c>, until it took the root on 2026-11-20. A link or a
/// bookmark is sent to the same address at the root, with its query, when that address is made of plain path
/// segments; anything else is sent to the root itself. Three addresses are answered here instead, all with fixed
/// content:
/// <list type="bullet">
/// <item><c>/native/</c> with no query, the start page of an app installed from there: a page that links the manifest
/// below and moves itself to the root after two seconds (<c>retired-native-page.html</c>).</item>
/// <item><c>/native/manifest.webmanifest</c>: the installed app's manifest under its old id, with its start and scope
/// moved to the root, so the browser keeps the app and opens it at the root in its own window
/// (<c>retired-native-manifest.webmanifest</c>).</item>
/// <item><c>/native/service-worker.js</c>: a worker that removes the old one and what it stored, and hands its push
/// subscription to the client at the root (<c>retired-native-worker.js</c>).</item>
/// </list>
/// </summary>
internal sealed partial class RetiredNativeAddress(RequestDelegate next)
{
    #region Private Fields

    private const string Root = "/";

    private const string BasePath = "/native";

    private const string MovingPagePath = "/";

    private static readonly FrozenDictionary<string, RetiredFile> files = new Dictionary<string, RetiredFile>
    {
        ["/service-worker.js"] = new("retired-native-worker.js", "text/javascript"),
        ["/manifest.webmanifest"] = new("retired-native-manifest.webmanifest", "application/manifest+json"),
        [MovingPagePath] = new("retired-native-page.html", "text/html"),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    #endregion

    #region Public Methods

    public Task Invoke(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(BasePath, out var rest))
        {
            return next(context);
        }

        if (FileFor(context.Request, rest) is { } file)
        {
            return TypedResults.Text(file.Text, file.ContentType, Encoding.UTF8).ExecuteAsync(context);
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

    /// <summary>The fixed file a GET or HEAD asks for. The moving page only at <c>/native/</c> itself, with no query.</summary>
    private static RetiredFile? FileFor(HttpRequest request, PathString rest)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return null;
        }

        var path = rest.Value ?? string.Empty;

        if (path == MovingPagePath && request.QueryString.HasValue)
        {
            return null;
        }

        return files.GetValueOrDefault(path);
    }

    #endregion

    #region Nested Types

    /// <summary>A file built into the server, read once.</summary>
    private sealed class RetiredFile(string name, string contentType)
    {
        #region Private Fields

        private readonly Lazy<string> text = new(() => Read(name));

        #endregion

        #region Properties

        public string Text => text.Value;

        public string ContentType => contentType;

        #endregion

        #region Private Methods

        private static string Read(string name)
        {
            var resource = $"Drover.Server.Clients.{name}";
            using var stream = typeof(RetiredNativeAddress).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"The server was built without {resource}.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        #endregion
    }

    #endregion
}
