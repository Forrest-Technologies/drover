namespace Drover.Contracts.Bots;

/// <summary>
/// Everything that defines a bot, as the editor reads it. Enumerations travel as lower-case words.
/// </summary>
public sealed record BotDetail(
    Guid Id,
    string Name,
    string Handle,
    BotAvatarBody Avatar,
    string DescriptionForBots,
    string Instructions,
    Guid ModelConnectionId,
    string Model,
    ModelSettingsBody ModelSettings,
    MemorySettingsBody MemorySettings,
    string SandboxPolicy,
    SandboxSettingsBody SandboxSettings,
    IReadOnlyList<Guid> SandboxSharedWith,
    string Status,
    Guid? ParentBotId,
    int Depth,
    BuildLimitsBody BuildLimits,
    Guid? CreatedByUserId,
    Guid? CreatedByBotId);

/// <summary>A bot's picture: an uploaded file, or a short symbol. With neither, the bot's drawn character is shown.</summary>
public sealed record BotAvatarBody(string? Emoji, Guid? FileId);

/// <summary>Model options in provider-neutral names. A null leaves the choice to the model.</summary>
public sealed record ModelSettingsBody(double? Temperature, int? MaxOutputTokens, string? ReasoningEffort, string WebSearch, bool StructuredOutput);

/// <summary>Whether the bot remembers, and how it keeps a long thread inside the model's context.</summary>
public sealed record MemorySettingsBody(bool Enabled, string Compaction, int? CompactAtTokens, int? KeepRecentMessages);

/// <summary>What a bot's sandbox may reach, how long it keeps running after its last use, and whether it has a screen.</summary>
public sealed record SandboxSettingsBody(string Network, IReadOnlyList<string> AllowedHosts, int? IdleTimeoutMinutes, bool Desktop = false);

/// <summary>How many bots this bot may build, and how deep beneath itself. A null leaves the space's cap.</summary>
public sealed record BuildLimitsBody(int? MaxChildren, int? MaxDepthBelow);

/// <summary>
/// What a sandbox on this server may be, so the editor offers only that: whether the server has a sandbox provider at
/// all, whether a sandbox may reach the open internet (deployment rule AllowOpenSandboxNetwork), and the most idle
/// minutes before it pauses (MaxSandboxIdleMinutes), and whether a sandbox may have a screen (AllowDesktopSandboxes).
/// Never which provider, and never its key.
/// </summary>
public sealed record BotSandboxOptions(bool Available, bool OpenNetwork, int MaxIdleMinutes, bool Desktop);
