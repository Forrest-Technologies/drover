namespace Drover.Engine.Tools;

/// <summary>
/// What signing in to a tool server needs to know about this server: the address people's browsers reach it at.
/// The tool server's sign-in page sends the browser back there, so it cannot be guessed from a request.
/// </summary>
public sealed class ToolSignInOptions
{
    #region Private Fields

    public const string MissingPublicUrl =
        "Signing in to a tool server needs this server's public address. Set DROVER_PUBLIC_URL to the address people open Drover at.";

    public const string CallbackPath = "/oauth/mcp/callback";

    public const string ClientDocumentPath = "/oauth/client.json";

    /// <summary>The host of the address that brings a browser back into a native app after a sign-in.</summary>
    public const string AppReturnHost = "tool-sign-in";

    /// <summary>
    /// The address schemes of Drover's own native apps: the Android preview shell, and the native client's Android
    /// build. Each is its app's package name. A browser is sent into an app only through one of these, never through a
    /// scheme a request names freely.
    /// </summary>
    public static readonly FrozenSet<string> AppSchemes = FrozenSet.ToFrozenSet(
        ["com.forresttechnologies.drover.preview", "com.forresttechnologies.drover.client"], StringComparer.Ordinal);

    /// <summary>
    /// The paths of the browser clients this server serves: the web client, and the native client's browser build
    /// (<c>NativeClientFiles.BasePath</c> in the server). A browser that started a sign-in in one comes back to it.
    /// </summary>
    public static readonly FrozenSet<string> WebPaths = FrozenSet.ToFrozenSet(["/", "/native/"], StringComparer.Ordinal);

    #endregion

    #region Properties


    /// <summary>The address people open Drover at, such as <c>https://drover.example.com</c>.</summary>
    public Uri? PublicUrl { get; set; }

    /// <summary>Where a tool server's sign-in page sends the browser back to. Null until a usable public address is set.</summary>
    public Uri? RedirectUrl => Origin is { } origin ? new Uri(origin, CallbackPath) : null;

    /// <summary>
    /// Where this server publishes who it is, for a tool server that reads such a document. Only a public
    /// <c>https</c> address can be fetched by somebody else's server, so there is none without one.
    /// </summary>
    public Uri? ClientDocumentUrl => Origin is { Scheme: "https", IsLoopback: false } origin ? new Uri(origin, ClientDocumentPath) : null;

    private Uri? Origin =>
        PublicUrl is { IsAbsoluteUri: true, Scheme: "https" or "http", UserInfo: "" } url ? new Uri(url.GetLeftPart(UriPartial.Authority)) : null;

    #endregion
}
