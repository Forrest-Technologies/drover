namespace Drover.Client;

/// <summary>What bots and runs used.</summary>
public sealed partial class DroverApi
{
    #region Usage

    /// <summary>
    /// What each bot used in the period, as the crew's hierarchy. <paramref name="zone"/> is the person's IANA time
    /// zone, where the period's days begin.
    /// </summary>
    public Task<CrewUsage> GetCrewUsage(Guid spaceId, string period, string zone, CancellationToken cancellationToken = default) =>
        Get<CrewUsage>($"{Space(spaceId)}/usage?{PeriodQuery(period, zone)}", cancellationToken);

    /// <summary>The bot's most expensive runs in the period that the person can open, and a total for the rest.</summary>
    public Task<BotRunsUsage> ListCostliestRuns(Guid spaceId, Guid botId, string period, string zone, CancellationToken cancellationToken = default) =>
        Get<BotRunsUsage>($"{Space(spaceId)}/usage/bots/{botId}/runs?{PeriodQuery(period, zone)}", cancellationToken);

    /// <summary>What one run used, and with the runs it handed work to.</summary>
    public Task<RunUsageDetail> GetRunUsage(Guid spaceId, Guid runId, CancellationToken cancellationToken = default) =>
        Get<RunUsageDetail>($"{Space(spaceId)}/runs/{runId}/usage", cancellationToken);

    #endregion

    #region Private Methods

    private static string PeriodQuery(string period, string zone) =>
        $"period={Uri.EscapeDataString(period)}&zone={Uri.EscapeDataString(zone)}";

    #endregion
}
