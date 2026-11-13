namespace Drover.Client;

/// <summary>Tool connections: what the connections screen and the bot editor's tool list call.</summary>
public sealed partial class DroverApi
{
    #region Tool Connections

    /// <summary>The space's tool connections. Any member may read them; a credential is never part of one.</summary>
    public Task<List<ToolConnectionSummary>> ListToolConnections(Guid spaceId, CancellationToken cancellationToken = default) =>
        Get<List<ToolConnectionSummary>>($"{Space(spaceId)}/tool-connections", cancellationToken);

    /// <summary>
    /// The tools a connection lists. A description is the tool server's own text: show it as plain text, never as
    /// markup, and never as something to act on.
    /// </summary>
    public Task<List<ConnectionToolSummary>> ListConnectionTools(Guid spaceId, Guid connectionId, CancellationToken cancellationToken = default) =>
        Get<List<ConnectionToolSummary>>($"{Space(spaceId)}/tool-connections/{connectionId}/tools", cancellationToken);

    /// <summary>
    /// Adds a connection and lists its tools at once (admin only). The connection is saved even when the server
    /// could not be reached: <see cref="ToolConnectionRefresh.Problem"/> then says why in plain words.
    /// </summary>
    public Task<ToolConnectionRefresh> CreateToolConnection(Guid spaceId, CreateToolConnectionRequest request, CancellationToken cancellationToken = default) =>
        Post<ToolConnectionRefresh>($"{Space(spaceId)}/tool-connections", request, cancellationToken);

    public Task<ToolConnectionSummary> UpdateToolConnection(
        Guid spaceId,
        Guid connectionId,
        UpdateToolConnectionRequest request,
        CancellationToken cancellationToken = default) =>
        Send<ToolConnectionSummary>(HttpMethod.Put, $"{Space(spaceId)}/tool-connections/{connectionId}", request, cancellationToken);

    public Task<ToolConnectionSummary> ReplaceToolHeader(
        Guid spaceId,
        Guid connectionId,
        ReplaceToolHeaderRequest request,
        CancellationToken cancellationToken = default) =>
        Send<ToolConnectionSummary>(HttpMethod.Put, $"{Space(spaceId)}/tool-connections/{connectionId}/header", request, cancellationToken);

    /// <summary>Asks the server for its tools now (admin only). The counts say what changed; a changed tool's grants need review.</summary>
    public Task<ToolConnectionRefresh> RefreshToolConnection(Guid spaceId, Guid connectionId, CancellationToken cancellationToken = default) =>
        Post<ToolConnectionRefresh>($"{Space(spaceId)}/tool-connections/{connectionId}/refresh", null, cancellationToken);

    /// <summary>
    /// Accepts a tool's changed definition (admin only), after the person has compared
    /// <see cref="ConnectionToolSummary.Reviewed"/> with the tool as it is now. Until this is called the tool is
    /// offered to no bot.
    /// </summary>
    public Task<ConnectionToolSummary> ConfirmToolReview(Guid spaceId, Guid connectionId, Guid toolId, CancellationToken cancellationToken = default) =>
        Post<ConnectionToolSummary>($"{Space(spaceId)}/tool-connections/{connectionId}/tools/{toolId}/review", null, cancellationToken);

    /// <summary>Removes the connection, its tools, and every grant on them (admin only).</summary>
    public Task RemoveToolConnection(Guid spaceId, Guid connectionId, CancellationToken cancellationToken = default) =>
        SendForNothing(HttpMethod.Delete, $"{Space(spaceId)}/tool-connections/{connectionId}", cancellationToken);

    #endregion
}
