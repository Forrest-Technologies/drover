namespace Drover.Ui.Starters;

/// <summary>
/// What a bot used in a period. <see cref="Cost"/> is null when no model it ran on has a known price.
/// <see cref="LeavesOutUnpriced"/> is true when the cost is a sum that some unpriced use is missing from.
/// </summary>
public sealed record UsageFigure(decimal? Cost, long TokensIn, long TokensOut, bool LeavesOutUnpriced = false);

/// <summary>One bot's usage and the bots beneath it. <see cref="WithBeneath"/> is null for a bot with none beneath.</summary>
public sealed record UsageNode(
    Guid BotId,
    string BotName,
    string Model,
    UsageFigure Own,
    UsageFigure? WithBeneath,
    IReadOnlyList<UsageNode> Children);

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

    /// <summary>True when nothing ran in the period, so the screen says so and shows no table.</summary>
    public static bool IsEmpty(IEnumerable<UsageNode> crew) =>
        crew.All(bot => (bot.WithBeneath ?? bot.Own) is { TokensIn: 0, TokensOut: 0 });

    #endregion

    #region Private Methods

    private static decimal Weight(UsageNode bot) => (bot.WithBeneath ?? bot.Own).Cost ?? -1;

    private static long TokenWeight(UsageNode bot)
    {
        var figure = bot.WithBeneath ?? bot.Own;
        return figure.TokensIn + figure.TokensOut;
    }

    #endregion
}
