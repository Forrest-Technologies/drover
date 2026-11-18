namespace Drover.Native.Platform;

/// <summary>
/// Reads a cookie of the page through the web client's own <c>cookies.js</c>, shared. Used for the antiforgery token,
/// which the server leaves readable for this purpose; the session cookie is not readable by script at all.
/// </summary>
internal static partial class PageCookies
{
    #region Private Fields

    private const string Module = "drover-cookies";

    // Relative to the runtime's own folder, _framework, beside which the page's files sit.
    private const string ModulePath = "../cookies.js";

    private static Task<JSObject>? loaded;

    #endregion

    #region Public Methods

    public static async ValueTask<string?> Read(string name, CancellationToken cancellationToken)
    {
        await (loaded ??= JSHost.ImportAsync(Module, ModulePath, CancellationToken.None)).WaitAsync(cancellationToken);
        return ReadWith(name);
    }

    #endregion

    #region Private Methods

    [JSImport("read", Module)]
    private static partial string? ReadWith(string name);

    #endregion
}
