namespace Drover.Data.Repositories;

/// <summary>
/// The sums of a set of model calls: tokens in and out, the cost of the calls priced in the currency asked for, and
/// how many calls were priced in it and how many were not (no known price, or another currency).
/// </summary>
public sealed record UsageSum(long InputTokens, long OutputTokens, decimal Cost, int PricedCalls, int UnpricedCalls)
{
    #region Properties

    public static UsageSum None { get; } = new(0, 0, 0, 0, 0);

    public bool IsEmpty => PricedCalls + UnpricedCalls == 0;

    #endregion

    #region Public Methods

    public UsageSum Plus(UsageSum other) =>
        new(
            InputTokens + other.InputTokens,
            OutputTokens + other.OutputTokens,
            Cost + other.Cost,
            PricedCalls + other.PricedCalls,
            UnpricedCalls + other.UnpricedCalls);

    #endregion
}

/// <summary>What one bot's model calls added up to.</summary>
public sealed record BotUsageSum(Guid BotId, UsageSum Usage);

/// <summary>What one run's model calls added up to, with the conversation it ran in and when its first call was made.</summary>
public sealed record RunUsageSum(Guid RunId, Guid ThreadId, Guid ThreadBotId, bool IsMain, string? ThreadTitle, DateTimeOffset At, UsageSum Usage);

/// <summary>
/// Reads <c>usage_records</c> as sums, one statement per question. Every method runs inside a tenant scope the caller
/// opened. A principal user id narrows the calls to the runs a person's own runs made; null counts
/// every run of the space. A conversation the viewer cannot see stays hidden by the threads policy, so a run in one
/// is never listed on its own.
/// </summary>
public sealed class UsageRepository(DroverDbContext db)
{
    #region Reads

    /// <summary>The currency most of the calls since then were priced in, or null when there were none.</summary>
    public Task<string?> FindCurrency(DateTimeOffset since, Guid? principalUserId, CancellationToken cancellationToken) =>
        Currency(Calls(since, principalUserId), cancellationToken);

    /// <summary>The currency most of the runs' calls were priced in, or null when they made none.</summary>
    public Task<string?> FindCurrency(IReadOnlyCollection<Guid> runIds, CancellationToken cancellationToken) =>
        Currency(db.UsageRecords.Where(usage => runIds.Contains(usage.RunId)), cancellationToken);

    /// <summary>Each bot that made a call since then, and what its calls add up to.</summary>
    public Task<List<BotUsageSum>> ListBotSums(DateTimeOffset since, Guid? principalUserId, string currency, CancellationToken cancellationToken) =>
        Calls(since, principalUserId)
            .GroupBy(usage => usage.BotId)
            .Select(calls => new BotUsageSum(
                calls.Key,
                new UsageSum(
                    calls.Sum(usage => usage.InputTokens),
                    calls.Sum(usage => usage.OutputTokens),
                    calls.Sum(usage => usage.Currency == currency ? usage.Cost : null) ?? 0,
                    calls.Count(usage => usage.Cost != null && usage.Currency == currency),
                    calls.Count(usage => usage.Cost == null || usage.Currency != currency))))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The bot's runs since then that cost the most, in conversations the viewer can see: priced runs first, by cost,
    /// then by tokens, then by id so the order holds between loads.
    /// </summary>
    public async Task<List<RunUsageSum>> ListCostliestRuns(
        Guid botId,
        DateTimeOffset since,
        Guid? principalUserId,
        string currency,
        int count,
        CancellationToken cancellationToken)
    {
        var sums = await (
                from usage in Calls(since, principalUserId)
                where usage.BotId == botId
                join run in db.Runs on usage.RunId equals run.Id
                join thread in db.Threads on run.ThreadId equals thread.Id
                group usage by new { usage.RunId, run.ThreadId, ThreadBotId = thread.BotId, thread.IsMain, thread.Title } into calls
                select new
                {
                    calls.Key,
                    At = calls.Min(usage => usage.CreatedAt),
                    InputTokens = calls.Sum(usage => usage.InputTokens),
                    OutputTokens = calls.Sum(usage => usage.OutputTokens),
                    Cost = calls.Sum(usage => usage.Currency == currency ? usage.Cost : null) ?? 0,
                    Priced = calls.Count(usage => usage.Cost != null && usage.Currency == currency),
                    Unpriced = calls.Count(usage => usage.Cost == null || usage.Currency != currency),
                })
            .OrderBy(run => run.Priced == 0)
            .ThenByDescending(run => run.Cost)
            .ThenByDescending(run => run.InputTokens + run.OutputTokens)
            .ThenBy(run => run.Key.RunId)
            .Take(count)
            .ToListAsync(cancellationToken);

        return
        [
            .. sums.Select(run => new RunUsageSum(
                run.Key.RunId,
                run.Key.ThreadId,
                run.Key.ThreadBotId,
                run.Key.IsMain,
                run.Key.Title,
                run.At,
                new UsageSum(run.InputTokens, run.OutputTokens, run.Cost, run.Priced, run.Unpriced))),
        ];
    }

    /// <summary>
    /// How many of the bot's runs since then are in conversations the viewer cannot see, and what they add up to
    /// together. Nothing about any one of them leaves this method.
    /// </summary>
    public async Task<(int Runs, UsageSum Usage)> SumUnopenedRuns(
        Guid botId,
        DateTimeOffset since,
        Guid? principalUserId,
        string currency,
        CancellationToken cancellationToken)
    {
        var hidden = Calls(since, principalUserId)
            .Where(usage => usage.BotId == botId)
            .Join(db.Runs, usage => usage.RunId, run => run.Id, (usage, run) => new { Usage = usage, run.ThreadId })
            .Where(call => !db.Threads.Any(thread => thread.Id == call.ThreadId))
            .Select(call => call.Usage);
        var runs = await hidden.Select(usage => usage.RunId).Distinct().CountAsync(cancellationToken);
        return (runs, runs == 0 ? UsageSum.None : await Sum(hidden, currency, cancellationToken));
    }

    /// <summary>What the runs' calls add up to.</summary>
    public Task<UsageSum> SumRuns(IReadOnlyCollection<Guid> runIds, string currency, CancellationToken cancellationToken) =>
        Sum(db.UsageRecords.Where(usage => runIds.Contains(usage.RunId)), currency, cancellationToken);

    #endregion

    #region Private Methods

    private IQueryable<UsageRecord> Calls(DateTimeOffset since, Guid? principalUserId) =>
        principalUserId is { } userId
            ? db.UsageRecords.AsNoTracking().Where(usage =>
                usage.CreatedAt >= since && db.Runs.Any(run => run.Id == usage.RunId && run.PrincipalUserId == userId))
            : db.UsageRecords.AsNoTracking().Where(usage => usage.CreatedAt >= since);

    private static Task<string?> Currency(IQueryable<UsageRecord> calls, CancellationToken cancellationToken) =>
        calls.GroupBy(usage => usage.Currency)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefaultAsync(cancellationToken);

    private static async Task<UsageSum> Sum(IQueryable<UsageRecord> calls, string currency, CancellationToken cancellationToken) =>
        await calls
            .GroupBy(_ => 1)
            .Select(group => new UsageSum(
                group.Sum(usage => usage.InputTokens),
                group.Sum(usage => usage.OutputTokens),
                group.Sum(usage => usage.Currency == currency ? usage.Cost : null) ?? 0,
                group.Count(usage => usage.Cost != null && usage.Currency == currency),
                group.Count(usage => usage.Cost == null || usage.Currency != currency)))
            .SingleOrDefaultAsync(cancellationToken)
        ?? UsageSum.None;

    #endregion
}
