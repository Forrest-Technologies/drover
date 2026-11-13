namespace Drover.Data.Repositories;

/// <summary>A rule with the bot it was written on, for a bot that follows it.</summary>
public sealed record InheritedRule(BotRule Rule, string BotName, int Distance);

/// <summary>
/// Bot rules. A bot follows its own rules and those of every bot above it, which is one join on the ancestors table.
/// </summary>
public sealed class BotRuleRepository(DroverDbContext db)
{
    #region Public Methods

    public void Add(BotRule rule) => db.BotRules.Add(rule);

    public void Remove(BotRule rule) => db.BotRules.Remove(rule);

    /// <summary>The bot's own rules, tracked, in their order.</summary>
    public Task<List<BotRule>> GetRules(Guid botId, CancellationToken cancellationToken) =>
        db.BotRules.Where(rule => rule.BotId == botId).OrderBy(rule => rule.Position).ThenBy(rule => rule.Id).ToListAsync(cancellationToken);

    /// <summary>
    /// Every rule the bot follows: those of the bots above it first, the furthest first, then its own, each bot's in
    /// its order. A bot that was removed still binds the bots beneath it.
    /// </summary>
    public async Task<List<InheritedRule>> ListRulesAlongPath(Guid botId, CancellationToken cancellationToken)
    {
        var rows = await db.BotAncestors.AsNoTracking()
            .Where(path => path.DescendantBotId == botId)
            .Join(db.BotRules.AsNoTracking(), path => path.AncestorBotId, rule => rule.BotId, (path, rule) => new { path.Distance, Rule = rule })
            .Join(db.Bots.AsNoTracking(), found => found.Rule.BotId, bot => bot.Id, (found, bot) => new { found.Distance, found.Rule, bot.Name })
            .ToListAsync(cancellationToken);

        return [.. rows
            .OrderByDescending(found => found.Distance)
            .ThenBy(found => found.Rule.Position)
            .ThenBy(found => found.Rule.Id)
            .Select(found => new InheritedRule(found.Rule, found.Name, found.Distance))];
    }

    #endregion
}
