namespace Drover.Ui.Pages.Tools;

/// <summary>
/// Registered by a native shell only: the address scheme a browser uses to come back into this app after a tool
/// server's sign-in page. The web app registers none, and its browser comes back to the web app itself.
/// </summary>
public sealed record AppSignInReturn(string Scheme);
