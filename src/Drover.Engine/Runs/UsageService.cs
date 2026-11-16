namespace Drover.Engine.Runs;

/// <summary>
/// Usage as people read it: per bot for a period, alone and with the bots beneath it; a bot's most expensive runs;
/// and one run with the runs it handed work to. Every figure is a sum of <c>usage_records</c>. Owners and admins read
/// the whole space and a member only the runs that act for them. A run in a conversation the viewer cannot open is
/// counted in totals and never shown on its own. Runs inside the caller's tenant scope.
/// </summary>
public sealed class UsageService(
    UsageRepository usage,
    BotRepository bots,
    RunRepository runs,
    ThreadRepository threads,
    TimeProvider time)
{
    #region Private Fields

    /// <summary>The currency a period with no calls is shown in, the catalog's own default.</summary>
    private const string DefaultCurrency = "USD";

    #endregion

    #region Properties

    /// <summary>How many of a bot's runs are listed: enough to see what made it expensive.</summary>
    public const int CostliestRunCount = 10;

    #endregion

    #region Public Methods

    /// <summary>What each bot used in the period, as the hierarchy the bots form. Bots that used nothing, alone and beneath, are left out.</summary>
    public async Task<CrewUsage> GetCrewUsage(string period, TimeZoneInfo zone, Guid userId, bool isSpaceAdmin, CancellationToken cancellationToken)
    {
        var since = Since(period, zone, time.GetUtcNow());
        var principal = PrincipalFilter(userId, isSpaceAdmin);
        var currency = await usage.FindCurrency(since, principal, cancellationToken) ?? DefaultCurrency;
        var sums = (await usage.ListBotSums(since, principal, currency, cancellationToken)).ToDictionary(sum => sum.BotId, sum => sum.Usage);
        IReadOnlyList<UsageNode> crew = sums.Count == 0 ? [] : Crew(await bots.ListBotsWithRemoved(cancellationToken), sums);
        return new CrewUsage(period, since, currency, crew);
    }

    /// <summary>The bot's most expensive runs in the period that the viewer can open, and a total for those they cannot.</summary>
    public async Task<BotRunsUsage> ListCostliestRuns(
        Guid botId,
        string period,
        TimeZoneInfo zone,
        Guid userId,
        bool isSpaceAdmin,
        CancellationToken cancellationToken)
    {
        var since = Since(period, zone, time.GetUtcNow());
        var principal = PrincipalFilter(userId, isSpaceAdmin);
        var currency = await usage.FindCurrency(since, principal, cancellationToken) ?? DefaultCurrency;
        var listed = await usage.ListCostliestRuns(botId, since, principal, currency, CostliestRunCount, cancellationToken);
        var (count, unopened) = await usage.SumUnopenedRuns(botId, since, principal, currency, cancellationToken);

        return new BotRunsUsage(
            botId,
            currency,
            [.. listed.Select(run => new RunCost(run.RunId, run.ThreadId, run.ThreadBotId, run.IsMain, run.ThreadTitle, run.At, Figure(run.Usage)))],
            count == 0 ? null : new UnopenedRuns(count, Figure(unopened)));
    }

    /// <summary>
    /// What the run used, and with every run beneath it. Null when the viewer cannot open the run's conversation, or
    /// is a member and the run does not act for them.
    /// </summary>
    public async Task<RunUsageDetail?> GetRunUsage(Guid runId, Guid userId, bool isSpaceAdmin, CancellationToken cancellationToken)
    {
        var run = await runs.FindRun(runId, cancellationToken);

        if (run is null || (!isSpaceAdmin && run.PrincipalUserId != userId) || await threads.FindThread(run.ThreadId, cancellationToken) is null)
        {
            return null;
        }

        List<Guid> tree = [.. (await runs.ListSelfAndDescendants(runId, cancellationToken)).Select(found => found.Id)];
        var currency = await usage.FindCurrency(tree, cancellationToken) ?? DefaultCurrency;
        var own = await usage.SumRuns([runId], currency, cancellationToken);
        var withBeneath = tree.Count > 1 ? Figure(await usage.SumRuns(tree, currency, cancellationToken)) : null;
        return new RunUsageDetail(runId, currency, Figure(own), withBeneath);
    }

    /// <summary>
    /// The first instant of the period in the viewer's time zone: the start of today there, or of the day 6 or 29
    /// days before it, so the last 7 days are 7 whole days counting today.
    /// </summary>
    public static DateTimeOffset Since(string period, TimeZoneInfo zone, DateTimeOffset now)
    {
        var days = period switch
        {
            UsagePeriods.Today => 0,
            UsagePeriods.Week => 6,
            UsagePeriods.Month => 29,
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Not a usage period."),
        };
        var start = TimeZoneInfo.ConvertTime(now, zone).Date.AddDays(-days);
        return new DateTimeOffset(start, zone.GetUtcOffset(start)).ToUniversalTime();
    }

    #endregion

    #region Private Methods

    private static Guid? PrincipalFilter(Guid userId, bool isSpaceAdmin) => isSpaceAdmin ? null : userId;

    /// <summary>
    /// The crew as a tree of figures, built from one sum per bot. A bot's figure with the bots beneath it is its own
    /// plus theirs; a bot appears when it or a bot beneath it used something.
    /// </summary>
    private static List<UsageNode> Crew(List<Bot> all, Dictionary<Guid, UsageSum> sums)
    {
        var known = all.Select(bot => bot.Id).ToHashSet();
        var beneath = all.Where(bot => bot.ParentBotId is { } parent && known.Contains(parent)).ToLookup(bot => bot.ParentBotId!.Value);
        return
        [
            .. all.Where(bot => bot.ParentBotId is not { } parent || !known.Contains(parent))
                .Select(Branch)
                .OfType<CrewBranch>()
                .Select(branch => branch.Node),
        ];

        CrewBranch? Branch(Bot bot)
        {
            var own = sums.GetValueOrDefault(bot.Id, UsageSum.None);
            List<CrewBranch> children = [.. beneath[bot.Id].Select(Branch).OfType<CrewBranch>()];
            var subtree = children.Aggregate(own, (sum, child) => sum.Plus(child.Subtree));
            return subtree.IsEmpty
                ? null
                : new CrewBranch(
                    new UsageNode(
                        bot.Id,
                        bot.Name,
                        bot.Model,
                        Figure(own),
                        children.Count == 0 ? null : Figure(subtree),
                        [.. children.Select(child => child.Node)],
                        bot.Status is BotStatus.Removed),
                    subtree);
        }
    }

    /// <summary>
    /// A sum as people read it: no cost at all when nothing in it had a price in the period's currency, never a zero,
    /// and what it leaves out named: calls with no known price, and calls priced in another currency.
    /// </summary>
    private static UsageFigure Figure(UsageSum sum) =>
        new(
            sum is { PricedCalls: 0 } && !sum.IsEmpty ? null : sum.Cost,
            sum.InputTokens,
            sum.OutputTokens,
            sum.UnpricedCalls > 0,
            sum.OtherCurrencyCalls > 0 ? sum.OtherCurrencies : null);

    #endregion

    #region Types

    private sealed record CrewBranch(UsageNode Node, UsageSum Subtree);

    #endregion
}
