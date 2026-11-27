namespace Drover.Data.Repositories;

/// <summary>
/// The space's decision model connection. Every method runs inside a tenant scope the service has opened, so the space
/// is the one the scope is for and no other.
/// </summary>
public sealed class DecisionConnectionRepository(DroverDbContext db)
{
    #region Public Methods

    /// <summary>The space's connection, untracked, or null when it has none.</summary>
    public Task<DecisionConnection?> FindConnection(CancellationToken cancellationToken) =>
        db.DecisionConnections.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    /// <summary>The space's connection, tracked so the caller can change it, or null.</summary>
    public Task<DecisionConnection?> GetConnection(CancellationToken cancellationToken) =>
        db.DecisionConnections.SingleOrDefaultAsync(cancellationToken);

    public void Add(DecisionConnection connection) => db.DecisionConnections.Add(connection);

    public void Remove(DecisionConnection connection) => db.DecisionConnections.Remove(connection);

    /// <summary>
    /// How many enabled rules in the space hold a sentence to judge, on bots that are not removed: what stops being
    /// checked when the connection goes.
    /// </summary>
    public async Task<int> CountJudgedRules(CancellationToken cancellationToken)
    {
        var enabled = await db.BotRules.AsNoTracking()
            .Where(rule => rule.Enabled && db.Bots.Any(bot => bot.Id == rule.BotId && bot.Status != BotStatus.Removed))
            .Select(rule => rule.Conditions)
            .ToListAsync(cancellationToken);
        return enabled.Count(conditions => conditions.Any(condition => condition.Kind == RuleConditionKind.Judged));
    }

    #endregion
}
