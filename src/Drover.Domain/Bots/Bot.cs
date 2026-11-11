namespace Drover.Domain.Bots;

/// <summary>One bot in a space's tree. Its permissions are grants, narrowed by its ancestors' grants.</summary>
public sealed class Bot : ISpaceOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid? ParentBotId { get; set; }

    public required string Name { get; set; }

    public required string Handle { get; set; }

    public BotAvatar Avatar { get; set; } = new();

    /// <summary>What other bots read to decide whether to delegate to this one.</summary>
    public string DescriptionForBots { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public Guid ModelConnectionId { get; set; }

    public required string Model { get; set; }

    public ModelSettings ModelSettings { get; set; } = new();

    public MemorySettings MemorySettings { get; set; } = new();

    public SandboxPolicy SandboxPolicy { get; set; } = SandboxPolicy.None;

    public SandboxSettings SandboxSettings { get; set; } = new();

    public BotStatus Status { get; set; } = BotStatus.Active;

    public int Depth { get; set; }

    public BuildLimits BuildLimits { get; set; } = new();

    public Guid? CreatedByUserId { get; set; }

    public Guid? CreatedByBotId { get; set; }
}
