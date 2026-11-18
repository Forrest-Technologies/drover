namespace Drover.Presentation.ViewModels;

/// <summary>
/// A bot of the usage table at its depth in the crew, with every figure said in words. A bot with bots beneath it has
/// two figures in each cell: its own and its total with them.
/// </summary>
public sealed record UsageTableRow(
    Guid BotId,
    string BotName,
    int Hue,
    int Depth,
    string Address,
    bool Chosen,
    bool Removed,
    string CostOwn,
    string? CostBeneath,
    string? LeftOut,
    string TokensInOwn,
    string? TokensInBeneath,
    string TokensOutOwn,
    string? TokensOutBeneath,
    string Model)
{
    #region Properties

    public static string OwnSuffix => " on its own";

    public static string BeneathSuffix => " with the bots beneath it";

    /// <summary>The name a screen reader gives the bot's cell: a removed bot says so.</summary>
    public string Label => Removed ? $"{BotName}, removed" : BotName;

    /// <summary>The bot's own tokens in, as read out: "on its own" when a second figure follows.</summary>
    public string TokensInOwnWords => $"{TokensInOwn}{(TokensInBeneath is null ? string.Empty : OwnSuffix)}";

    public string TokensOutOwnWords => $"{TokensOutOwn}{(TokensOutBeneath is null ? string.Empty : OwnSuffix)}";

    public string? TokensInBeneathWords => TokensInBeneath is null ? null : $"{TokensInBeneath}{BeneathSuffix}";

    public string? TokensOutBeneathWords => TokensOutBeneath is null ? null : $"{TokensOutBeneath}{BeneathSuffix}";

    #endregion

    #region Public Methods

    /// <summary>A row of the crew as the table shows it, its name linking to <paramref name="address"/>.</summary>
    public static UsageTableRow Of(UsageRow row, string currency, string address, bool chosen)
    {
        var bot = row.Bot;
        var beneath = bot.WithBeneath;

        // The figure with the bots beneath holds the bot's own, so one sentence covers whichever leaves something out.
        var leftOut = UsageWords.LeftOut(beneath ?? bot.Own) ?? UsageWords.LeftOut(bot.Own);

        return new(
            bot.BotId,
            bot.BotName,
            BotIdentity.Hue(bot.BotId),
            row.Depth,
            address,
            chosen,
            bot.IsRemoved,
            beneath is null ? UsageWords.Cost(bot.Own, currency) : $"{UsageWords.Cost(bot.Own, currency)}{OwnSuffix}",
            beneath is null ? null : $"{UsageWords.Cost(beneath, currency)}{BeneathSuffix}",
            leftOut,
            UsageWords.Tokens(bot.Own.InputTokens),
            beneath is null ? null : UsageWords.Tokens(beneath.InputTokens),
            UsageWords.Tokens(bot.Own.OutputTokens),
            beneath is null ? null : UsageWords.Tokens(beneath.OutputTokens),
            bot.Model);
    }

    #endregion
}
