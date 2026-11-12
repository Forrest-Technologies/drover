namespace Drover.Engine.Tools;

/// <summary>Maps tool connections and their tools to what the API carries. Nothing here can reach a credential.</summary>
public static class ToolViews
{
    #region Public Methods

    public static ToolConnectionSummary ToSummary(this ToolConnection connection) =>
        new(
            connection.Id,
            connection.Name,
            connection.Slug,
            connection.Url,
            WireName(connection.AuthKind),
            WireName(connection.Trust),
            WireName(connection.ProtocolMode),
            WireName(connection.Status),
            connection.NegotiatedProtocol,
            connection.LastListedAt,
            connection.LastError);

    public static ConnectionToolSummary ToSummary(this Tool tool, ToolConnection connection) =>
        new(
            tool.Id,
            connection.Id,
            tool.Name,
            ToolNames.ForModel(connection.Slug, tool.Name),
            tool.Title,
            tool.Description,
            WireName(tool.Sensitivity),
            WireName(tool.State),
            tool.LastSeenAt);

    public static ToolConnectionRefresh ToResponse(this ToolRefresh refresh) =>
        new(
            refresh.Connection.ToSummary(),
            [.. refresh.Tools.Select(tool => tool.ToSummary(refresh.Connection))],
            refresh.Failure is null ? null : refresh.Message,
            refresh.Change?.Added ?? 0,
            refresh.Change?.Changed ?? 0,
            refresh.Change?.Missing ?? 0,
            refresh.Change?.GrantsForReview ?? 0);

    #endregion

    #region Private Methods

    /// <summary>An enum value as the API writes it: snake case, as the database stores it.</summary>
    private static string WireName<T>(T value)
        where T : struct, Enum =>
        string.Concat(value.ToString().Select((character, index) =>
            char.IsUpper(character) && index > 0 ? $"_{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));

    #endregion
}
