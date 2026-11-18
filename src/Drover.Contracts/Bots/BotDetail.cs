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
    RunLimitsBody RunLimits,
    Guid? CreatedByUserId,
    Guid? CreatedByBotId);

/// <summary>A bot's picture: an uploaded file, or a short symbol. With neither, the bot's drawn character is shown.</summary>
public sealed record BotAvatarBody(string? Emoji, Guid? FileId);

/// <summary>Model options in provider-neutral names. A null leaves the choice to the model.</summary>
public sealed record ModelSettingsBody(double? Temperature, int? MaxOutputTokens, string? ReasoningEffort, string WebSearch, bool StructuredOutput);

/// <summary>
/// Whether the bot keeps notes about the person it works for, how long a note and all of them together may be (a null
/// leaves the server's limit), and how it keeps a long thread inside the model's context.
/// </summary>
public sealed record MemorySettingsBody(
    bool Enabled,
    string Compaction,
    int? CompactAtTokens,
    int? KeepRecentMessages,
    int? MaxNoteCharacters = null,
    int? MaxNotesCharacters = null);

/// <summary>
/// What a bot's sandbox may reach, how long it keeps running after its last use, whether it has a screen, and which
/// model reads that screen for the bot (null: the bot's own model).
/// </summary>
public sealed record SandboxSettingsBody(
    string Network,
    IReadOnlyList<string> AllowedHosts,
    int? IdleTimeoutMinutes,
    bool Desktop = false,
    ScreenReaderBody? ScreenReader = null);

/// <summary>A model of the space, by connection and name, that reads the sandbox's screen for the bot.</summary>
public sealed record ScreenReaderBody(Guid ModelConnectionId, string Model);

/// <summary>How many bots this bot may build, and how deep beneath itself. A null leaves the space's cap.</summary>
public sealed record BuildLimitsBody(int? MaxChildren, int? MaxDepthBelow);

/// <summary>
/// What one piece of the bot's work may use before it is stopped: model calls, tokens read and written together, and
/// times it hands work to a bot beneath it. A null leaves the space's limit.
/// </summary>
public sealed record RunLimitsBody(int? MaxSteps, int? MaxTokens, int? MaxDelegations);

/// <summary>
/// The limits of the bot's space on one piece of work, so the editor says what leaving a limit empty means and the
/// most a bot's own limit may be. A bot's limit only lowers these.
/// </summary>
public sealed record BotRunLimitOptions(int MaxSteps, int MaxTokens, int MaxDelegations);

/// <summary>
/// What a sandbox on this server may be, so the editor offers only that: whether the server has a sandbox provider at
/// all, whether a sandbox may reach the open internet (deployment rule AllowOpenSandboxNetwork), and the most idle
/// minutes before it pauses (MaxSandboxIdleMinutes), and whether a sandbox may have a screen (AllowDesktopSandboxes).
/// Never which provider, and never its key.
/// </summary>
public sealed record BotSandboxOptions(bool Available, bool OpenNetwork, int MaxIdleMinutes, bool Desktop);
