namespace Drover.Contracts.Runs;

/// <summary>
/// What a bot, a run, or a tree of runs used. <see cref="Cost"/> counts the calls priced in the period's currency, and
/// is null when none was. <see cref="LeavesOutUnpriced"/> is true when some calls were on a model whose price is not
/// known. <see cref="UncountedCurrencies"/> names the other currencies some calls were priced in, such as "EUR", or is
/// null when there were none; those calls are not in <see cref="Cost"/> either.
/// </summary>
public sealed record UsageFigure(
    decimal? Cost,
    long InputTokens,
    long OutputTokens,
    bool LeavesOutUnpriced = false,
    string? UncountedCurrencies = null);

/// <summary>
/// One bot's usage and the bots beneath it. <see cref="WithBeneath"/> is null for a bot with none beneath it that
/// used anything. <see cref="Model"/> is the model the bot is set to now. <see cref="IsRemoved"/> is true for a bot
/// removed since: what it used still counts.
/// </summary>
public sealed record UsageNode(
    Guid BotId,
    string BotName,
    string Model,
    UsageFigure Own,
    UsageFigure? WithBeneath,
    IReadOnlyList<UsageNode> Children,
    bool IsRemoved = false);

/// <summary>
/// What the crew used in a period that began at <see cref="Since"/>, in the viewer's time zone. <see cref="Currency"/>
/// is the one the catalog's prices are in. <see cref="Crew"/> holds the bots at the top, each with the bots beneath
/// it that used anything; it is empty when nothing ran.
/// </summary>
public sealed record CrewUsage(string Period, DateTimeOffset Since, string Currency, IReadOnlyList<UsageNode> Crew);

/// <summary>
/// One run of a bot and what it used, in a conversation the viewer can open: the thread, the bot it belongs to, and
/// whether it is that bot's main conversation. <see cref="At"/> is when its first model call was made.
/// </summary>
public sealed record RunCost(
    Guid RunId,
    Guid ThreadId,
    Guid ThreadBotId,
    bool IsMain,
    string? ThreadTitle,
    DateTimeOffset At,
    UsageFigure Usage);

/// <summary>
/// A bot's most expensive runs in a period, among those in conversations the viewer can open. <see cref="Unopened"/>
/// totals the runs in conversations the viewer cannot open, with nothing about any one of them, or is null when
/// there are none.
/// </summary>
public sealed record BotRunsUsage(Guid BotId, string Currency, IReadOnlyList<RunCost> Runs, UnopenedRuns? Unopened);

/// <summary>How many runs are in conversations the viewer cannot open, and what they used together.</summary>
public sealed record UnopenedRuns(int Count, UsageFigure Usage);

/// <summary>What one run used, and with the runs it handed work to. <see cref="Tree"/> is null for a run that handed none.</summary>
public sealed record RunUsageDetail(Guid RunId, string Currency, UsageFigure Own, UsageFigure? Tree);

/// <summary>The periods usage is read for: today, the last 7 days, and the last 30 days, each counted in whole days.</summary>
public static class UsagePeriods
{
    #region Properties

    public const string Today = "today";

    public const string Week = "7d";

    public const string Month = "30d";

    public static IReadOnlyList<string> All { get; } = [Today, Week, Month];

    #endregion
}
