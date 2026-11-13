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

    // A removed connection keeps its row, switched off, because the record of what bots did with its tools points
    // at it. Nothing below returns such a row: to every caller a removed connection is gone.

    public Task<ToolConnection?> FindConnection(Guid id, CancellationToken cancellationToken) =>
        db.ToolConnections.AsNoTracking()
            .SingleOrDefaultAsync(connection => connection.Id == id && connection.Status != ConnectionStatus.Disabled, cancellationToken);

    /// <summary>The tracked row, for a caller that changes it.</summary>
    public Task<ToolConnection?> GetConnection(Guid id, CancellationToken cancellationToken) =>
        db.ToolConnections.SingleOrDefaultAsync(connection => connection.Id == id && connection.Status != ConnectionStatus.Disabled, cancellationToken);

    public Task<List<ToolConnection>> ListConnections(CancellationToken cancellationToken) =>
        db.ToolConnections.AsNoTracking()
            .Where(connection => connection.Status != ConnectionStatus.Disabled)
            .OrderBy(connection => connection.Name)
            .ThenBy(connection => connection.Id)
            .ToListAsync(cancellationToken);

    public Task<List<ToolConnection>> ListConnections(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        db.ToolConnections.AsNoTracking()
            .Where(connection => ids.Contains(connection.Id) && connection.Status != ConnectionStatus.Disabled)
            .ToListAsync(cancellationToken);

    public Task<bool> IsSlugTaken(string slug, CancellationToken cancellationToken) =>
        db.ToolConnections.AnyAsync(connection => connection.Slug == slug, cancellationToken);

    /// <summary>
    /// Removes a connection for good, as far as anyone can use it: every grant on its tools and every credential
    /// row is deleted, its tools are marked missing, and the connection is switched off. The connection and tool
    /// rows stay, because <c>tool_calls</c> points at them and the record of what a bot did is never rewritten.
    /// The caller forgets the credentials first, with <see cref="ForgetCredentials"/>.
    /// </summary>
    public async Task Remove(ToolConnection connection, CancellationToken cancellationToken)
    {
        var toolIds = db.Tools.Where(tool => tool.ToolConnectionId == connection.Id).Select(tool => tool.Id);
        await db.ToolGrants.Where(grant => toolIds.Contains(grant.ToolId)).ExecuteDeleteAsync(cancellationToken);
        await db.Tools.Where(tool => tool.ToolConnectionId == connection.Id)
            .ExecuteUpdateAsync(tool => tool.SetProperty(row => row.State, AvailabilityState.Missing), cancellationToken);
        connection.Status = ConnectionStatus.Disabled;
        connection.LastError = null;
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

    /// <summary>The credentials of the connections that the transaction can see: the space's, and the caller's own.</summary>
    public Task<List<ToolConnectionCredential>> ListCredentials(IReadOnlyCollection<Guid> connectionIds, CancellationToken cancellationToken) =>
        db.ToolConnectionCredentials.AsNoTracking().Where(credential => connectionIds.Contains(credential.ToolConnectionId)).ToListAsync(cancellationToken);

    public void Remove(ToolConnectionCredential credential) => db.ToolConnectionCredentials.Remove(credential);

    /// <summary>
    /// Deletes every credential of the connection and the secret of each, whoever they belong to, and returns how
    /// many. One person's credential is invisible in another's transaction, so this crosses audiences inside the
    /// one space, through <c>sys.forget_tool_credentials</c>, which works on the transaction's own space only.
    /// Call it before <see cref="Remove(ToolConnection, CancellationToken)"/>, in the same transaction.
    /// </summary>
    public async Task<int> ForgetCredentials(Guid connectionId, CancellationToken cancellationToken) =>
        await db.Database
            .SqlQuery<int>($"SELECT sys.forget_tool_credentials({connectionId}) AS \"Value\"")
            .SingleAsync(cancellationToken);

    public void Add(OAuthFlow flow) => db.OAuthFlows.Add(flow);

    /// <summary>Forgets a sign-in in progress. Only the person who started it can see the row, so only they remove it.</summary>
    public Task<int> RemoveFlow(string stateHash, CancellationToken cancellationToken) =>
        db.OAuthFlows.Where(flow => flow.StateHash == stateHash).ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Whose sign-in a provider's callback belongs to. The callback has no space, so this goes through
    /// <c>sys.resolve_oauth_state</c>, which returns ids only and nothing for a flow that has run out of time.
    /// </summary>
    public async Task<SignInInProgress?> ResolveFlow(string stateHash, CancellationToken cancellationToken) =>
        (await db.Database
            .SqlQuery<SignInInProgress>(
                $"""
                SELECT space_id, user_id, tool_connection_id
                FROM sys.resolve_oauth_state({stateHash})
                """)
            .ToListAsync(cancellationToken)).SingleOrDefault();

    /// <summary>
    /// The sign-ins, in every space, whose access token runs out before the given moment. This crosses spaces, so
    /// it goes through <c>sys.tool_sign_ins_running_out</c>, which returns ids only: the caller then works on each
    /// one inside that space's own tenant scope, as the person the sign-in belongs to.
    /// </summary>
    public Task<List<SignInRunningOut>> ListSignInsRunningOut(DateTimeOffset before, int limit, CancellationToken cancellationToken) =>
        db.Database
            .SqlQuery<SignInRunningOut>(
                $"""
                SELECT space_id, audience_user_id, tool_connection_id, credential_id
                FROM sys.tool_sign_ins_running_out({before}, {limit})
                """)
            .ToListAsync(cancellationToken);

    public void Add(Tool tool) => db.Tools.Add(tool);

    /// <summary>The tools a connection has listed, tracked, so the caller can bring them up to date.</summary>
    public Task<List<Tool>> GetTools(Guid connectionId, CancellationToken cancellationToken) =>
        db.Tools.Where(tool => tool.ToolConnectionId == connectionId).OrderBy(tool => tool.Name).ToListAsync(cancellationToken);

    public Task<List<Tool>> ListTools(Guid connectionId, CancellationToken cancellationToken) =>
        db.Tools.AsNoTracking().Where(tool => tool.ToolConnectionId == connectionId).OrderBy(tool => tool.Name).ToListAsync(cancellationToken);

    public Task<Tool?> GetTool(Guid connectionId, Guid toolId, CancellationToken cancellationToken) =>
        db.Tools.SingleOrDefaultAsync(tool => tool.Id == toolId && tool.ToolConnectionId == connectionId, cancellationToken);

    /// <summary>Of the tools, the ones somebody was granted: a change to one of those is a change a person must look at.</summary>
    public async Task<HashSet<Guid>> ListGrantedTools(IReadOnlyCollection<Guid> toolIds, CancellationToken cancellationToken) =>
        [.. await db.ToolGrants.Where(grant => toolIds.Contains(grant.ToolId)).Select(grant => grant.ToolId).Distinct().ToListAsync(cancellationToken)];

    /// <summary>
    /// Of the tools, the ones with a grant still waiting for a person to review a changed definition. A grant of any
    /// bot counts: the definition changed for all of them at once, and one review settles it for all.
    /// </summary>
    public async Task<HashSet<Guid>> ListToolsNeedingReview(IReadOnlyCollection<Guid> toolIds, CancellationToken cancellationToken) =>
        [
            .. await db.ToolGrants
                .Where(grant => toolIds.Contains(grant.ToolId) && grant.ReviewState == ReviewState.NeedsReview)
                .Select(grant => grant.ToolId)
                .Distinct()
                .ToListAsync(cancellationToken),
        ];

    /// <summary>A person has reviewed the tool's definition: every grant on it counts as current again.</summary>
    public Task<int> ConfirmReview(Guid toolId, CancellationToken cancellationToken) =>
        db.ToolGrants
            .Where(grant => grant.ToolId == toolId && grant.ReviewState == ReviewState.NeedsReview)
            .ExecuteUpdateAsync(grant => grant.SetProperty(row => row.ReviewState, ReviewState.Current), cancellationToken);

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

/// <summary>A sign-in to a tool server that is waiting for its callback: where it was started, by whom, and for which connection.</summary>
public sealed record SignInInProgress(Guid SpaceId, Guid UserId, Guid ToolConnectionId);

/// <summary>A sign-in whose access token is about to run out: ids only. The audience is the person it belongs to, or everyone.</summary>
public sealed record SignInRunningOut(Guid SpaceId, Guid AudienceUserId, Guid ToolConnectionId, Guid CredentialId);
