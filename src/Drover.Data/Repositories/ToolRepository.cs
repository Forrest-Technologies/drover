namespace Drover.Data.Repositories;

/// <summary>One tool a bot may use, with the effective grant: the minimum along the bot's path to the root.</summary>
public sealed record GrantedTool(Tool Tool, GrantEffect Effect);

/// <summary>
/// The tool catalog and grants. Access is always decided through <see cref="ListGrantedTools"/> or
/// <see cref="FindEffectiveGrant"/>, which ask the database's <c>effective_grants</c> function; a grant row alone
/// never decides it. Runs inside the caller's tenant scope.
/// </summary>
public sealed class ToolRepository(DroverDbContext db)
{
    #region Private Fields

    private const long CatalogLockSeed = 0x44524F56_45520003;

    #endregion

    #region Public Methods

    public void Add(Tool tool) => db.Tools.Add(tool);

    public void Add(ToolGrant grant) => db.ToolGrants.Add(grant);

    /// <summary>
    /// Makes two changes to one space's catalog take turns until the transaction ends, so both cannot add the same
    /// tool. The lock is keyed by the space, so spaces never wait on each other.
    /// </summary>
    public Task LockCatalog(CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended(sys.current_space()::text, {CatalogLockSeed}))",
            cancellationToken);

    /// <summary>The space's built-in tool rows, tracked, so the caller can refresh them.</summary>
    public Task<List<Tool>> ListBuiltinTools(CancellationToken cancellationToken) =>
        db.Tools.Where(tool => tool.SourceKind == ToolSourceKind.Builtin).ToListAsync(cancellationToken);

    /// <summary>Every tool the bot has an effective grant for, whatever the effect.</summary>
    /// <summary>The tools that tool connections of this space have listed, by connection and name.</summary>
    public Task<List<Tool>> ListConnectionTools(CancellationToken cancellationToken) =>
        db.Tools.AsNoTracking()
            .Where(tool => tool.SourceKind != ToolSourceKind.Builtin)
            .OrderBy(tool => tool.ToolConnectionId)
            .ThenBy(tool => tool.Name)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The rows a bot holds itself, tracked. This is what an editor shows and changes. It never decides access: a row
    /// counts only as far as the path to the root allows, which is what <see cref="ListEffectiveGrants"/> answers.
    /// </summary>
    public Task<List<ToolGrant>> ListOwnGrants(Guid botId, CancellationToken cancellationToken) =>
        db.ToolGrants.Where(grant => grant.BotId == botId).ToListAsync(cancellationToken);

    public void Remove(ToolGrant grant) => db.ToolGrants.Remove(grant);

    public async Task<List<GrantedTool>> ListGrantedTools(Guid botId, CancellationToken cancellationToken)
    {
        var grants = await ListEffectiveGrants(botId, cancellationToken);
        var ids = grants.Keys.ToList();
        var tools = await db.Tools.AsNoTracking().Where(tool => ids.Contains(tool.Id)).ToListAsync(cancellationToken);
        return [.. tools.OrderBy(tool => tool.Name, StringComparer.Ordinal).Select(tool => new GrantedTool(tool, grants[tool.Id]))];
    }

    /// <summary>The bot's effective grant for one tool right now, or null when it has none.</summary>
    public async Task<GrantEffect?> FindEffectiveGrant(Guid botId, Guid toolId, CancellationToken cancellationToken) =>
        (await ListEffectiveGrants(botId, cancellationToken)).TryGetValue(toolId, out var effect) ? effect : null;

    /// <summary>What the bot may do with each tool once every bot above it is counted, by tool id.</summary>
    public async Task<Dictionary<Guid, GrantEffect>> ListEffectiveGrants(Guid botId, CancellationToken cancellationToken)
    {
        var rows = await db.Database
            .SqlQuery<EffectiveGrantRow>($"SELECT tool_id, effect FROM effective_grants({botId})")
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.ToolId, row => Enum.Parse<GrantEffect>(row.Effect, ignoreCase: true));
    }

    #endregion

    #region Nested Types

    private sealed record EffectiveGrantRow(Guid ToolId, string Effect);

    #endregion
}
