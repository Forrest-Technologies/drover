namespace Drover.Domain.Rules;

/// <summary>
/// A hard rule a person wrote on a bot. The bot and every bot beneath it follow it; a bot beneath can add rules
/// of its own and can never remove or loosen this one. Rules are data: they are checked by code, at call time.
/// </summary>
public sealed class BotRule : ISpaceOwned
{
    #region Properties

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SpaceId { get; set; }

    public Guid BotId { get; set; }

    public required string Name { get; set; }

    /// <summary>A rule that is switched off is kept and not checked.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The rule's place among the bot's own rules, from zero.</summary>
    public int Position { get; set; }

    public RuleCheckpoint Checkpoint { get; set; }

    public RuleEffect Effect { get; set; }

    /// <summary>Every one must hold for the rule to apply. None means the rule always applies.</summary>
    public List<RuleCondition> Conditions { get; set; } = [];

    public Guid? CreatedByUserId { get; set; }

    #endregion
}
