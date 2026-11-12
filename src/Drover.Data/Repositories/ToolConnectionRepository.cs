namespace Drover.Data.Repositories;

/// <summary>
/// Tool connections, their credentials, and the tools they list, as rows. Row-level security decides which
/// credential rows a transaction can see: a person's own and the space's shared ones, never another person's. So
/// nothing here filters by person to keep people apart; the database already has. Runs inside the caller's scope.
/// </summary>
public sealed class ToolConnectionRepository(DroverDbContext db)
{
    #region Connections

    public void Add(ToolConnection connection) => db.ToolConnections.Add(connection);

    public Task<ToolConnection?> FindConnection(Guid id, CancellationToken cancellationToken) =>
        db.ToolConnections.AsNoTracking().SingleOrDefaultAsync(connection => connection.Id == id, cancellationToken);

    /// <summary>The tracked row, for a caller that changes it.</summary>
    public Task<ToolConnection?> GetConnection(Guid id, CancellationToken cancellationToken) =>
        db.ToolConnections.SingleOrDefaultAsync(connection => connection.Id == id, cancellationToken);

    public Task<List<ToolConnection>> ListConnections(CancellationToken cancellationToken) =>
        db.ToolConnections.AsNoTracking().OrderBy(connection => connection.Name).ThenBy(connection => connection.Id).ToListAsync(cancellationToken);

    public Task<List<ToolConnection>> ListConnections(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.ToolConnections.AsNoTracking().Where(connection => ids.Contains(connection.Id)).ToListAsync(cancellationToken);

    public Task<bool> IsSlugTaken(string slug, CancellationToken cancellationToken) =>
        db.ToolConnections.AnyAsync(connection => connection.Slug == slug, cancellationToken);

    /// <summary>Removes a connection with everything that hangs on it: grants on its tools, the tools, its credentials.</summary>
    public async Task Remove(ToolConnection connection, CancellationToken cancellationToken)
    {
        var toolIds = db.Tools.Where(tool => tool.ToolConnectionId == connection.Id).Select(tool => tool.Id);
        await db.ToolGrants.Where(grant => toolIds.Contains(grant.ToolId)).ExecuteDeleteAsync(cancellationToken);
        await db.Tools.Where(tool => tool.ToolConnectionId == connection.Id).ExecuteDeleteAsync(cancellationToken);
        await db.ToolConnectionCredentials.Where(credential => credential.ToolConnectionId == connection.Id).ExecuteDeleteAsync(cancellationToken);
        db.ToolConnections.Remove(connection);
    }

    #endregion

    #region Credentials

    public void Add(ToolConnectionCredential credential) => db.ToolConnectionCredentials.Add(credential);

    /// <summary>
    /// The connection's credential for one audience: a person, or everyone. There is no fallback from one to the
    /// other here or anywhere: a person without a credential of their own has none. Tracked.
    /// </summary>
    public Task<ToolConnectionCredential?> GetCredential(Guid connectionId, Guid audienceUserId, CancellationToken cancellationToken) =>
        db.ToolConnectionCredentials.SingleOrDefaultAsync(
            credential => credential.ToolConnectionId == connectionId && credential.AudienceUserId == audienceUserId,
            cancellationToken);

    /// <summary>The credentials of a connection the transaction can see. Tracked.</summary>
    public Task<List<ToolConnectionCredential>> ListCredentials(Guid connectionId, CancellationToken cancellationToken) =>
        db.ToolConnectionCredentials.Where(credential => credential.ToolConnectionId == connectionId).ToListAsync(cancellationToken);

    #endregion

    #region Tools

    public void Add(Tool tool) => db.Tools.Add(tool);

    /// <summary>The tools a connection has listed, tracked, so the caller can bring them up to date.</summary>
    public Task<List<Tool>> GetTools(Guid connectionId, CancellationToken cancellationToken) =>
        db.Tools.Where(tool => tool.ToolConnectionId == connectionId).OrderBy(tool => tool.Name).ToListAsync(cancellationToken);

    public Task<List<Tool>> ListTools(Guid connectionId, CancellationToken cancellationToken) =>
        db.Tools.AsNoTracking().Where(tool => tool.ToolConnectionId == connectionId).OrderBy(tool => tool.Name).ToListAsync(cancellationToken);

    /// <summary>
    /// Marks every grant on the tools as needing review, which makes each behave as "ask" until a person confirms
    /// it. Returns how many grants changed.
    /// </summary>
    public Task<int> MarkGrantsForReview(IReadOnlyCollection<Guid> toolIds, CancellationToken cancellationToken) =>
        db.ToolGrants
            .Where(grant => toolIds.Contains(grant.ToolId) && grant.ReviewState != ReviewState.NeedsReview)
            .ExecuteUpdateAsync(grant => grant.SetProperty(row => row.ReviewState, ReviewState.NeedsReview), cancellationToken);

    #endregion
}
