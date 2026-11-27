namespace Drover.Domain.Bots;

/// <summary>One bot in a space's tree. Its permissions are grants, narrowed by its ancestors' grants.</summary>
public sealed class Bot : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid? ParentBotId { get; set; }

    public required string Name { get; set; }

    public required string Handle { get; set; }

    /// <summary>
    /// True when a person set the handle themselves, so it never follows the name when the bot is renamed, whatever it
    /// is spelled like. False for a handle the name made, with or without the number added to keep it unique. A bot
    /// that was there before this was recorded holds false and is judged by the spelling of its handle.
    /// </summary>
    public bool HandleChosen { get; set; }

    public BotAvatar Avatar { get; set; } = new();

    /// <summary>What other bots read to decide whether to delegate to this one.</summary>
    public string DescriptionForBots { get; set; } = string.Empty;

    public bool AcceptsBotRequests { get; set; } = true;

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

    public RunLimits RunLimits { get; set; } = new();

    public Guid? CreatedByUserId { get; set; }

    public Guid? CreatedByBotId { get; set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// A copy of the bot for one run that answers with <paramref name="model"/>, as a conversation that chose another
    /// of the bot's models does. The copy is not stored: the bot keeps its own model.
    /// </summary>
    public Bot AnsweringWith(string model)
    {
        var copy = (Bot)MemberwiseClone();
        copy.Model = model;
        return copy;
    }

    #endregion
}
