namespace Drover.Contracts.Bots;

/// <summary>
/// The editable definition of a bot. A part that is left out keeps its stored value.
/// <paramref name="HandleFollowsName"/> says the handle sent is the one the name makes, not one a person chose. True
/// says the handle was not chosen, and when the name changes and another bot holds that handle, the server adds
/// a number as it does for a new bot instead of refusing. False records that a person chose it: it never follows a
/// rename and is never renumbered, and a taken one is refused. Left out, a handle that changed is a choice and one that
/// did not keeps what is stored. A choice, once recorded, is never undone by an update.
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
