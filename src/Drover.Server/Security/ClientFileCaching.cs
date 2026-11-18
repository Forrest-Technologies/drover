namespace Drover.Server.Security;

/// <summary>
/// How long a browser, or a cache in front of the server, may keep a file of the web client. The server says so for
/// every one, because a response that says nothing is kept for as long as whatever sits in front decides: a content
/// network adds hours of its own, and a browser then runs the old client long after a new build is deployed.
/// </summary>
public static partial class ClientFileCaching
{
    #region Public Methods

    /// <summary>
    /// The policy for a request path outside the API. A framework file whose name carries a hash of its content
    /// never changes under that name, so it may be kept for good. Everything else (the page, the service worker and
    /// its list of files, scripts and styles with plain names) is asked of the server every time: the service worker
    /// keeps its own copy of the shell for opening offline, so nothing is lost by it. The native client's browser
    /// build, under <c>/native/</c>, follows the same rule, and everything in its package folder may be kept for good:
    /// the folder is named after a hash of the build, so a new build is a new folder.
    /// </summary>
    public static string For(PathString path) =>
        ((path.StartsWithSegments(FrameworkFiles) || path.StartsWithSegments("/native/_framework")) && NamedByContent().IsMatch(path.Value!))
            || NativePackage().IsMatch(path.Value ?? string.Empty)
            ? KeepForGood
            : AskEveryTime;

    #endregion

    #region Constants

    public const string KeepForGood = "public, max-age=31536000, immutable";

    public const string AskEveryTime = "no-cache, no-store";

    private const string FrameworkFiles = "/_framework";

    #endregion

    #region Private Methods

    // The build names a framework file after ten characters of a hash of its content: System.Linq.4jr7wme4t4.wasm.
    // A digit is required, so a plain ten-letter word in a name is not taken for a hash; a hash that happens to have
    // no digit is asked for every time, which costs a request and nothing else.
    [GeneratedRegex(@"\.(?=[a-z0-9]{10}\.)[a-z]*[0-9][a-z0-9]*\.[A-Za-z0-9]+(\.(br|gz))?$", RegexOptions.CultureInvariant)]
    private static partial Regex NamedByContent();

    // Uno places content in a directory named by the package hash. The loader and index stay revalidated.
    [GeneratedRegex(@"^/native/package_[a-fA-F0-9]{40}/[^/].*$", RegexOptions.CultureInvariant)]
    private static partial Regex NativePackage();

    #endregion
}
