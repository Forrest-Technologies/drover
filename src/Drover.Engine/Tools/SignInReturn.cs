namespace Drover.Engine.Tools;

/// <summary>
/// Where a person's browser goes when a tool server's sign-in page is done. It is read from what the client that
/// started the sign-in asked for, and is only ever one of a short list, so a sign-in never sends a browser to an
/// address a request chose freely.
/// </summary>
/// <param name="App">
/// The address that brings the browser into a native app, without its ticket: one of Drover's own app schemes, or
/// the desktop app's listener on the loopback address of the person's own computer. Null for a browser client.
/// </param>
/// <param name="WebPath">The path of the browser client to come back to.</param>
public sealed record SignInReturn(string? App, string WebPath)
{
    #region Private Fields

    /// <summary>The browser client at the root, for a request that names nowhere.</summary>
    public static readonly SignInReturn Web = new(null, "/");

    private const string Loopback = "http://127.0.0.1:";

    #endregion

    #region Public Methods

    /// <summary>Reads where a client asked to come back to. Null when it is not one of the places this server sends a browser.</summary>
    public static SignInReturn? Read(string? returnTo) => returnTo switch
    {
        null => Web,
        _ when ToolSignInOptions.WebPaths.Contains(returnTo) => new(null, returnTo),
        _ when ToolSignInOptions.AppSchemes.Contains(returnTo) => new($"{returnTo}://{ToolSignInOptions.AppReturnHost}", Web.WebPath),
        _ when IsDesktopListener(returnTo) => new($"{returnTo}/{ToolSignInOptions.AppReturnHost}", Web.WebPath),
        _ => null,
    };

    #endregion

    #region Private Methods

    // The desktop app listens on the IPv4 loopback address at a port the system gave it. Only that exact form is
    // taken: no host name, which could resolve elsewhere, and no path, query, or user part.
    private static bool IsDesktopListener(string returnTo) =>
        returnTo.StartsWith(Loopback, StringComparison.Ordinal)
        && int.TryParse(returnTo.AsSpan(Loopback.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
        && port is > 0 and <= ushort.MaxValue
        && returnTo == $"{Loopback}{port.ToString(CultureInfo.InvariantCulture)}";

    #endregion
}
