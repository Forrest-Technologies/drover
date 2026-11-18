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

    /// <summary>The name a screen reader gives the bot: a removed bot says so.</summary>
    public string Label => Removed ? $"{BotName}, removed" : BotName;

    public string TokensInOwnWords => $"{TokensInOwn}{(TokensInBeneath is null ? string.Empty : OwnSuffix)}";

    public string TokensOutOwnWords => $"{TokensOutOwn}{(TokensOutBeneath is null ? string.Empty : OwnSuffix)}";

    public string? TokensInBeneathWords => TokensInBeneath is null ? null : $"{TokensInBeneath}{BeneathSuffix}";

    public string? TokensOutBeneathWords => TokensOutBeneath is null ? null : $"{TokensOutBeneath}{BeneathSuffix}";

    #endregion
}

/// <summary>
/// Which bot is spending, to decide what to change. A table, cost first, the crew as the hierarchy it is, the
/// biggest spender first at every level. No chart and no totals card: the question is "which bot", and the order
/// answers it.
/// </summary>
public sealed class UsageTableModel
{
    #region Constructors

    /// <param name="crew">The bots at the top of the crew, each with the bots beneath it.</param>
    /// <param name="currency">The currency the catalog's prices are in, named beside every figure.</param>
    /// <param name="caption">What the table shows and for when, such as "What each bot used in the last 7 days".</param>
    /// <param name="botAddress">Where choosing a bot goes: its most expensive runs in the period.</param>
    /// <param name="chosenId">The bot whose runs are listed below the table, marked as the current one.</param>
    public UsageTableModel(IReadOnlyList<UsageNode> crew, string currency, string caption, Func<Guid, string> botAddress, Guid? chosenId = null)
    {
        Caption = caption;
        IsEmpty = UsageWords.IsEmpty(crew);
        Rows = IsEmpty ? [] : [.. UsageWords.Rows(crew).Select(row => RowOf(row, currency, botAddress, chosenId))];
    }

    #endregion

    #region Properties

    public static string EmptyNote => "No bot ran in this period.";

    public static IReadOnlyList<string> Columns => ["Bot", "Cost", "Tokens in", "Tokens out", "Model"];

    public string Caption { get; }

    /// <summary>True when nothing ran in the period, so the screen says so and shows no table.</summary>
    public bool IsEmpty { get; }

    public IReadOnlyList<UsageTableRow> Rows { get; }

    #endregion

    #region Private Methods

    private static UsageTableRow RowOf(UsageRow row, string currency, Func<Guid, string> botAddress, Guid? chosenId)
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
            botAddress(bot.BotId),
            bot.BotId == chosenId,
            bot.IsRemoved,
            beneath is null ? UsageWords.Cost(bot.Own, currency) : $"{UsageWords.Cost(bot.Own, currency)}{UsageTableRow.OwnSuffix}",
            beneath is null ? null : $"{UsageWords.Cost(beneath, currency)}{UsageTableRow.BeneathSuffix}",
            leftOut,
            UsageWords.Tokens(bot.Own.InputTokens),
            beneath is null ? null : UsageWords.Tokens(beneath.InputTokens),
            UsageWords.Tokens(bot.Own.OutputTokens),
            beneath is null ? null : UsageWords.Tokens(beneath.OutputTokens),
            bot.Model);
    }

    #endregion
}
