namespace Drover.Contracts.Bots;

/// <summary>
/// A bot's own sandbox as a person sees it. <see cref="Available"/> is whether the server has a sandbox provider and
/// <see cref="HasSandbox"/> whether the bot's sandbox policy gives it one. <see cref="State"/> is one of
/// <c>none</c> (not made yet, or turned off), <c>starting</c>, <c>running</c>, <c>paused</c>, or <c>replacing</c>
/// (an old one is still being taken down). <see cref="Desktop"/> is whether it has a screen, and
/// <see cref="BotWorking"/> whether a run of this bot is working right now. Never a provider id, a token, or which
/// provider it is.
/// </summary>
public sealed record BotSandboxStatus(
    bool Available,
    bool HasSandbox,
    string State,
    DateTimeOffset? LastUsedAt,
    bool Desktop,
    bool BotWorking);


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
