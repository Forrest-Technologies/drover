namespace Drover.Contracts.Bots;

/// <summary>
/// The editable definition of a bot. A part that is left out keeps its stored value.
/// </summary>
public sealed record UpdateBotRequest(
    string? Name,
    string? Handle,
    BotAvatarBody? Avatar,
    string? DescriptionForBots,
    string? Instructions,
    Guid? ModelConnectionId,
    string? Model,
    ModelSettingsBody? ModelSettings,
    MemorySettingsBody? MemorySettings,
    string? SandboxPolicy,
    SandboxSettingsBody? SandboxSettings,
    IReadOnlyList<Guid>? SandboxSharedWith,
    BuildLimitsBody? BuildLimits);
