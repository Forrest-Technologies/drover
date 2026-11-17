namespace Drover.Ui.Chat;

/// <summary>
/// What a request for a person at a bot's screen needs from the conversation it is in: the request the person holds
/// beside it, whether they can open the screen and where it is set up, and the two steps sent to the server.
/// </summary>
public sealed record ScreenTakeovers(
    Guid? HeldCallId,
    ScreenAccess Access,
    string? SetupHref,
    Func<Takeover, Task<string?>>? OpenScreen,
    Func<Guid, bool, string?, Task<string?>>? HandBack);
