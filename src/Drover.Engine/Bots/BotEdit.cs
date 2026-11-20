namespace Drover.Engine.Bots;

/// <summary>
/// Everything a person may set on a bot, already read into Drover's own types. A null enumeration is a word the
/// request spelled wrongly; the service says so.
/// </summary>
public sealed record BotEdit(
    string Name,
    string Handle,
    BotAvatar Avatar,
    string DescriptionForBots,
    string Instructions,
    Guid ModelConnectionId,
    string Model,
    ModelSettings? ModelSettings,
    MemorySettings? MemorySettings,
    SandboxPolicy? SandboxPolicy,
    SandboxSettings? SandboxSettings,
    IReadOnlyList<Guid> SandboxSharedWith,
    BuildLimits BuildLimits,
    RunLimits RunLimits)
{
    /// <summary>
    /// Whether the handle is the one the name makes (true), one a person chose (false), or the request did not say
    /// (null): then a handle that changed is a choice, and one that did not keeps what is stored. A bot's handle once
    /// chosen stays chosen.
    /// </summary>
    public bool? HandleFollowsName { get; init; }
}

/// <summary>
/// A bot to create: its name, what it is for as other bots read it, its instructions, and the model it talks to. It
/// goes beneath <see cref="ParentBotId"/>, or at the top of the tree when that is null.
/// </summary>
public sealed record NewBot(
    string Name,
    string DescriptionForBots,
    string Instructions,
    Guid ModelConnectionId,
    string? Model,
    Guid? ParentBotId)
{
    /// <summary>The bot starts paused: it does nothing until a person turns it on. For a bot whose instructions came from a file.</summary>
    public bool Paused { get; init; }
}

/// <summary>The result of a change to a bot. With neither a bot nor a problem, there is no such bot.</summary>
public sealed record BotChange(Bot? Bot, string? Problem)
{
    public static BotChange NotFound { get; } = new(null, null);

    /// <summary>The bots the changed bot's sandbox is shared with, as they are once the change is saved.</summary>
    public IReadOnlyList<Guid> SandboxSharedWith { get; init; } = [];

    /// <summary>The bots beneath a removed bot that went with it or moved up, by the choice the person made.</summary>
    public IReadOnlyList<Guid> Beneath { get; init; } = [];

    /// <summary>The changed bot as its editor shows it, or null when the change did not happen.</summary>
    public BotView? View => Bot is null ? null : new BotView(Bot, SandboxSharedWith);
}

/// <summary>
/// What happens to the bots beneath a bot that is removed: they are removed with it, or they are kept and move up
/// beneath its parent, or to the top. A bot with nothing beneath it needs no choice.
/// </summary>
public enum BotsBeneath
{
    Remove,
    Keep,
}

/// <summary>A bot with the bots its sandbox is shared with.</summary>
public sealed record BotView(Bot Bot, IReadOnlyList<Guid> SandboxSharedWith);
