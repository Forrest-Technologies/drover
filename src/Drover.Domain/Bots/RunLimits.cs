namespace Drover.Domain.Bots;

/// <summary>
/// Narrows the space's limits on one run of this bot, so a bot that runs away stops before it spends a person's
/// model credit. A null limit leaves the space's limit in force. Stored as JSON on the bot.
/// </summary>
public sealed record RunLimits
{
    #region Properties

    /// <summary>The most model calls one run of the bot makes.</summary>
    public int? MaxSteps { get; init; }

    /// <summary>The most tokens, read and written together, one run of the bot uses.</summary>
    public int? MaxTokens { get; init; }

    public bool UnlimitedTokensAcknowledged { get; init; }

    /// <summary>The most times one run of the bot hands work to a bot beneath it.</summary>
    public int? MaxDelegations { get; init; }

    #endregion
}
