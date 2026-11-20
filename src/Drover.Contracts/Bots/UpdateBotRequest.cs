namespace Drover.Contracts.Bots;

/// <summary>
/// The editable definition of a bot. A part that is left out keeps its stored value.
/// <paramref name="HandleFollowsName"/> says the handle sent is the one the new name makes, not one a person chose: when
/// the name changes and another bot holds that handle, the server adds a number as it does for a new bot instead of
/// refusing. A handle a person chose is never renumbered; a taken one is refused.
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
    BuildLimitsBody? BuildLimits,
    RunLimitsBody? RunLimits = null,
    bool? HandleFollowsName = null);
