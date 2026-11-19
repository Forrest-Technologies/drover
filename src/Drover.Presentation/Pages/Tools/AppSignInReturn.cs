namespace Drover.Ui.Pages.Tools;

/// <summary>
/// Registered by a client that is not the web app: where a browser comes back to after a tool server's sign-in page.
/// A device app gives its address scheme, the desktop app its listener on the loopback address, and the native
/// client's browser build the path it is served under. The web app registers none, and its browser comes back to the
/// web app itself.
/// </summary>
public sealed record AppSignInReturn(string ReturnTo);
