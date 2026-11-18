namespace Drover.Ui.Chat;

/// <summary>
/// The space's bots as the hierarchy they form: each bot under the one that manages it, in the order the server
/// lists them.
/// </summary>
public static class CrewTree
{
    #region Public Methods

    /// <summary>
    /// The bots at the top, each with its branch. A bot whose parent is not in the list (removed, or not shown to
    /// this person) stands at the top, so no bot is ever left out.
    /// </summary>
    public static IReadOnlyList<CrewNode> From(IReadOnlyList<BotSummary> bots)
    {
        var known = bots.Select(bot => bot.Id).ToHashSet();
        var beneath = bots
            .Where(bot => bot.ParentBotId is { } parent && known.Contains(parent) && parent != bot.Id)
            .ToLookup(bot => bot.ParentBotId!.Value);
        HashSet<Guid> placed = [];

        IReadOnlyList<CrewNode> Branch(IEnumerable<BotSummary> level) =>
            [.. level.Where(bot => placed.Add(bot.Id)).Select(bot => new CrewNode(bot, Branch(beneath[bot.Id])))];

        var top = Branch(bots.Where(bot => bot.ParentBotId is not { } parent || !known.Contains(parent) || parent == bot.Id));

        // Parents that point at each other have no top. They are still shown, at the top.
        return [.. top, .. Branch(bots.Where(bot => !placed.Contains(bot.Id)))];
    }

    /// <summary>Every bot under <paramref name="botId"/>, however far down, nearest first.</summary>
    public static IReadOnlyList<BotSummary> Beneath(IReadOnlyList<BotSummary> bots, Guid botId)
    {
        List<BotSummary> found = [];
        HashSet<Guid> above = [botId];

        // Each pass takes the bots whose parent is already in the branch, so a loop of parents ends when a pass adds none.
        while (bots.Where(bot => bot.ParentBotId is { } parent && above.Contains(parent) && above.Add(bot.Id)).ToList() is { Count: > 0 } level)
        {
            found.AddRange(level);
        }

        return found;
    }

    /// <summary>
    /// The bot a person talks to when they have not chosen one: the first bot at the top that can answer, then any
    /// that can, then the first there is.
    /// </summary>
    public static BotSummary? Lead(IReadOnlyList<BotSummary> bots)
    {
        var top = From(bots).Select(node => node.Bot).ToList();
        return top.FirstOrDefault(IsActive) ?? bots.FirstOrDefault(IsActive) ?? (bots is [var first, ..] ? first : null);
    }

    #endregion

    #region Private Methods

    private static bool IsActive(BotSummary bot) => bot.Status is "active";

    #endregion
}
