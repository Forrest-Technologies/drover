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
    BuildLimits BuildLimits);

/// <summary>The result of a change to a bot. With neither a bot nor a problem, there is no such bot.</summary>
public sealed record BotChange(Bot? Bot, string? Problem)
{
    public static BotChange NotFound { get; } = new(null, null);

    /// <summary>The bots the changed bot's sandbox is shared with, as they are once the change is saved.</summary>
    public IReadOnlyList<Guid> SandboxSharedWith { get; init; } = [];

    /// <summary>The changed bot as its editor shows it, or null when the change did not happen.</summary>
    public BotView? View => Bot is null ? null : new BotView(Bot, SandboxSharedWith);
}

/// <summary>A bot with the bots its sandbox is shared with.</summary>
public sealed record BotView(Bot Bot, IReadOnlyList<Guid> SandboxSharedWith);
