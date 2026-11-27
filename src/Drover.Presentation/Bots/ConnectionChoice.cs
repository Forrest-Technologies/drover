namespace Drover.Ui.Bots;

/// <summary>The connection a person would choose: one that works, then one not tested yet.</summary>
public static class ConnectionChoice
{
    #region Public Methods

    public static ModelConnectionSummary? Preferred(IReadOnlyList<ModelConnectionSummary> connections) =>
        connections
            .OrderBy(connection => StatusRank(connection.Status))
            .ThenByDescending(connection => connection.LastCheckedAt)
            .FirstOrDefault();

    public static bool IsChatModel(ModelCatalogResponse entry) =>
        entry.State is "available" && !entry.Traits.Contains("voice-only", StringComparer.Ordinal);

    #endregion

    #region Private Methods

    private static int StatusRank(string status) => status switch
    {
        ConnectionStatuses.Ready => 0,
        ConnectionStatuses.Pending => 1,
        _ => 2,
    };

    #endregion
}
