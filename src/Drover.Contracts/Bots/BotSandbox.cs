namespace Drover.Contracts.Bots;

/// <summary>
/// A bot's own sandbox as a person sees it. <see cref="Available"/> is whether the server has a sandbox provider and
/// <see cref="HasSandbox"/> whether the bot's sandbox policy gives it one. <see cref="State"/> is one of
/// <c>none</c> (not made yet, or turned off), <c>starting</c>, <c>running</c>, <c>paused</c>, or <c>replacing</c>
/// (an old one is still being taken down). <see cref="Desktop"/> is whether it has a screen,
/// <see cref="BotWorking"/> whether a run of this bot is working right now, and <see cref="DesktopOffered"/> whether
/// this server offers sandboxes with a screen at all, so a sandbox without one can say why. <see cref="StartFailed"/>
/// is set with <c>none</c> when the bot's newest sandbox was given up before the provider ever made it, so "no
/// sandbox yet" is not shown for a bot that tried to get one. <see cref="Hold"/> is set while a person holds the
/// screen because a bot that shares the sandbox asked them to. Never a provider id, a token, or which provider it is.
/// </summary>
public sealed record BotSandboxStatus(
    bool Available,
    bool HasSandbox,
    string State,
    DateTimeOffset? LastUsedAt,
    bool Desktop,
    bool BotWorking,
    bool DesktopOffered,
    bool StartFailed = false,
    SandboxHold? Hold = null);


/// <summary>
/// A person holds a sandbox's screen, at a bot's request: the run that waits for them, whether it is this person's,
/// and since when. No conversation content and no other person's name: an owner or admin may stop the run, and every
/// bot that shares the sandbox is refused until it is handed back.
/// </summary>
public sealed record SandboxHold(Guid RunId, bool ForYou, DateTimeOffset? Since);


/// <summary>
/// One mouse or keyboard action of a person on a bot's sandbox screen. <see cref="Kind"/> is <c>click</c>
/// (<see cref="X"/>, <see cref="Y"/>, <see cref="Button"/>: left, right or middle, <see cref="Twice"/>, a double click),
/// <c>type</c> (<see cref="Text"/>), <c>key</c> (<see cref="Keys"/>, pressed together, from the same list a bot
/// uses), or <c>scroll</c> (<see cref="Direction"/>: up or down, <see cref="Amount"/>). Coordinates are on the
/// sandbox's own screen, 1024 by 768.
/// </summary>
public sealed record SandboxInputRequest(
    string Kind,
    int? X = null,
    int? Y = null,
    string? Button = null,
    bool Twice = false,
    string? Text = null,
    IReadOnlyList<string>? Keys = null,
    string? Direction = null,
    int? Amount = null);


/// <summary>A command line a person runs in a bot's sandbox, as bash reads it.</summary>
public sealed record SandboxCommandRequest(string Command);


/// <summary>
/// What a person's command left: its exit status (none when it ran out of time and was stopped), its output, whether
/// some of it was left out, and whether the sandbox was new for it.
/// </summary>
public sealed record SandboxCommandResponse(int? ExitStatus, string Stdout, string Stderr, bool Truncated, bool TimedOut, bool NewSandbox);
