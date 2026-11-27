namespace Drover.Presentation.Platform;

/// <summary>
/// How this device opens the socket of a live call. A browser leaves <see cref="Connect"/> null: the page's own
/// socket carries its session cookie. A device, whose sign-in is not a cookie, opens the socket to the server its
/// person chose and adds its own credentials.
/// </summary>
public sealed record CallConnection(Func<Uri, CancellationToken, Task<WebSocket>>? Connect);
