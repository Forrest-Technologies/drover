namespace Drover.Ui.Usage;

/// <summary>A row of the usage table: a bot at its depth in the crew.</summary>
public sealed record UsageRow(UsageNode Bot, int Depth);

/// <summary>Usage in words and in order. No figure here is made up: an unknown price is said, never shown as zero.</summary>
public static class UsageWords
{
    #region Public Methods

    /// <summary>A cost with its currency, "USD 1.24". A real cost under a cent is "under USD 0.01".</summary>
    public static string Money(decimal? cost, string currency) => cost switch
    {
        null => "price not known",
        <= -0.01m => $"a credit of {currency} {(-cost.Value).ToString("N2", CultureInfo.InvariantCulture)}",
        < 0 => $"a credit under {currency} 0.01",
        0 => $"{currency} 0.00",
        < 0.01m => $"under {currency} 0.01",
        _ => $"{currency} {cost.Value.ToString("N2", CultureInfo.InvariantCulture)}",
    };

    /// <summary>
    /// What a sum is missing, said beside it, or null when it is missing nothing. A total that leaves something
    /// out always says so.
    /// </summary>
    public static string? LeftOut(UsageFigure figure) => figure.LeavesOutUnpriced && figure.Cost is not null
        ? "Not counting models whose price is not known"
        : null;

    public static string Tokens(long tokens) => tokens.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// The crew as table rows: each bot followed by the bots beneath it, and at every level the one that cost the
    /// most first. A bot is ordered by what it and the bots beneath it cost together, then by tokens when no
    /// price is known, then by name so the order never shifts between loads.
    /// </summary>
    public static IReadOnlyList<UsageRow> Rows(IEnumerable<UsageNode> crew)
    {
        List<UsageRow> rows = [];
        Add(crew, 0);
        return rows;

        void Add(IEnumerable<UsageNode> level, int depth)
        {
            foreach (var bot in level.OrderByDescending(Weight).ThenByDescending(TokenWeight).ThenBy(node => node.BotName, StringComparer.Ordinal))
            {
                rows.Add(new(bot, depth));
                Add(bot.Children, depth + 1);
            }
        }
    }

    /// <summary>The name of a period on its control: "Today", "Last 7 days", "Last 30 days".</summary>
    public static string PeriodName(string period) => period switch
    {
        UsagePeriods.Today => "Today",
        UsagePeriods.Week => "Last 7 days",
        _ => "Last 30 days",
    };

    /// <summary>The period inside a sentence: "today", "in the last 7 days".</summary>
    public static string During(string period) => period switch
    {
        UsagePeriods.Today => "today",
        UsagePeriods.Week => "in the last 7 days",
        _ => "in the last 30 days",
    };

    /// <summary>What the table shows and for when, read as its caption.</summary>
    public static string Caption(string period) => $"What each bot used {During(period)}";

    /// <summary>A figure in one line, cost first: "USD 0.02, 1,200 tokens in, 340 out".</summary>
    public static string Figure(UsageFigure figure, string currency) =>
        $"{Money(figure.Cost, currency)}, {Tokens(figure.InputTokens)} tokens in, {Tokens(figure.OutputTokens)} out";

    /// <summary>The heading over a bot's most expensive runs.</summary>
    public static string RunsTitle(string botName, string period) => $"{botName}'s most expensive runs {During(period)}";

    /// <summary>What is said when the bot made no call itself that the person can open: the figure was the bots beneath it.</summary>
    public static string NoRuns(string botName, string period) =>
        $"{botName} made no model calls of its own {During(period)} in a conversation you can open.";

    /// <summary>When a run made its first call and where, as the name of the link to its conversation.</summary>
    public static string RunPlace(RunCost run) =>
        $"{run.At.ToLocalTime().ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture)} in {(run.IsMain ? "the main conversation" : run.ThreadTitle ?? "a side chat")}";

    /// <summary>The runs in conversations the person cannot open, as one total that says nothing about any one of them.</summary>
    public static string Unopened(UnopenedRuns unopened, string currency) =>
        unopened.Count == 1
            ? $"One more run is in a conversation you cannot open: {Figure(unopened.Usage, currency)}."
            : $"{unopened.Count} more runs are in conversations you cannot open. Together: {Figure(unopened.Usage, currency)}.";

    /// <summary>The line in a reply's details: what the run used, and with the bots it asked when it asked any.</summary>
    public static IReadOnlyList<string> Reply(RunUsageDetail detail)
    {
        List<string> lines = [$"This reply: {Figure(detail.Own, detail.Currency)}."];

        if (detail.Tree is { } tree)
        {
            lines.Add($"With the bots it asked: {Figure(tree, detail.Currency)}.");
        }

        if (LeftOut(detail.Tree ?? detail.Own) is { } leftOut)
        {
            lines.Add($"{leftOut}.");
        }

        return lines;
    }

    /// <summary>True when nothing ran in the period, so the screen says so and shows no table.</summary>
    public static bool IsEmpty(IEnumerable<UsageNode> crew) =>
        crew.All(bot => (bot.WithBeneath ?? bot.Own) is { InputTokens: 0, OutputTokens: 0 });

    #endregion

    #region Private Methods

    private static decimal Weight(UsageNode bot) => (bot.WithBeneath ?? bot.Own).Cost ?? -1;

    private static long TokenWeight(UsageNode bot)
    {
        var figure = bot.WithBeneath ?? bot.Own;
        return figure.InputTokens + figure.OutputTokens;
    }

    #endregion
}
