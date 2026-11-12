namespace Drover.Engine.Tools;

/// <summary>What folding a server's tool list into the catalog changed.</summary>
public sealed record CatalogChange(int Added, int Changed, int Missing, int GrantsForReview);

/// <summary>
/// Keeps a space's catalog rows in step with the tools that exist: the built-in ones this server ships, and the ones
/// a tool server lists. A grant always points at a catalog row, so every tool is a row. A run reads these rows and
/// never asks a server what it offers, so a bot uses the definitions a person saw. Runs in the caller's tenant scope.
/// </summary>
public sealed class ToolCatalog(ToolRepository tools, ToolConnectionRepository connections, IEnumerable<BuiltinTool> builtins, TimeProvider time)
{
    #region Private Fields

    /// <summary>The most tools one connection may add to the catalog. A server chooses how many it lists; this server chooses how many it keeps.</summary>
    public const int MaxToolsPerConnection = 500;

    public const int MaxDescriptionLength = 8000;

    public const int MaxSchemaLength = 65_536;

    private const int MaxNameLength = 200;

    #endregion

    #region Public Methods

    /// <summary>Adds the rows that are missing, refreshes the ones whose definition changed, and returns all of them.</summary>
    public async Task<IReadOnlyList<Tool>> EnsureBuiltins(CancellationToken cancellationToken)
    {
        await tools.LockCatalog(cancellationToken);
        var existing = (await tools.ListBuiltinTools(cancellationToken)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var now = time.GetUtcNow();
        List<Tool> rows = [];

        foreach (var builtin in builtins)
        {
            if (!existing.TryGetValue(builtin.Name, out var row))
            {
                row = new Tool { SourceKind = ToolSourceKind.Builtin, Name = builtin.Name, DefinitionHash = builtin.DefinitionHash, FirstSeenAt = now };
                tools.Add(row);
            }

            row.Title = builtin.Title;
            row.Description = builtin.Description;
            row.InputSchema = builtin.InputSchema;
            row.DefinitionHash = builtin.DefinitionHash;
            row.Sensitivity = builtin.Sensitivity;
            row.State = AvailabilityState.Available;
            row.LastSeenAt = now;
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Folds what a tool server listed into the connection's catalog rows.
    /// <list type="bullet">
    /// <item>A new tool gets a row. No bot is granted it: a person grants it after reading it.</item>
    /// <item>A tool the server no longer lists is marked missing. Its grants stay, so it works again if it returns unchanged.</item>
    /// <item>A tool whose definition changed is updated, and every grant on it is marked for review, which makes it
    /// behave as "ask". A server cannot quietly turn a tool a person approved into something else.</item>
    /// </list>
    /// A server's claim that a tool only reads sets the sensitivity of a new row. It never lowers the sensitivity of
    /// an existing one: only a person does that.
    /// </summary>
    public async Task<CatalogChange> FoldServerTools(ToolConnection connection, IReadOnlyList<ListedTool> listed, CancellationToken cancellationToken)
    {
        await tools.LockCatalog(cancellationToken);
        var existing = (await connections.GetTools(connection.Id, cancellationToken)).ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        var now = time.GetUtcNow();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<Guid> changed = [];
        var added = 0;

        foreach (var tool in listed.Where(IsStorable).Take(MaxToolsPerConnection))
        {
            if (!seen.Add(tool.Name))
            {
                continue;
            }

            var hash = HashOf(tool);
            var claimed = tool.ClaimsReadOnly && !tool.ClaimsDestructive ? ToolSensitivity.Read : ToolSensitivity.Write;

            if (!existing.TryGetValue(tool.Name, out var row))
            {
                row = new Tool
                {
                    SourceKind = ToolSourceKind.Mcp,
                    ToolConnectionId = connection.Id,
                    Name = tool.Name,
                    DefinitionHash = hash,
                    Sensitivity = claimed,
                    FirstSeenAt = now,
                };
                connections.Add(row);
                added++;
            }
            else if (row.DefinitionHash != hash)
            {
                changed.Add(row.Id);
                row.Sensitivity = (ToolSensitivity)Math.Max((int)row.Sensitivity, (int)claimed);
            }

            row.Title = Shorten(tool.Title, MaxNameLength);
            row.Description = Shorten(tool.Description, MaxDescriptionLength) ?? string.Empty;
            row.InputSchema = tool.InputSchema;
            row.Annotations = tool.Annotations;
            row.DefinitionHash = hash;
            row.State = AvailabilityState.Available;
            row.LastSeenAt = now;
        }

        var missing = existing.Values.Where(row => !seen.Contains(row.Name) && row.State is AvailabilityState.Available).ToList();

        foreach (var row in missing)
        {
            row.State = AvailabilityState.Missing;
        }

        var forReview = changed.Count == 0 ? 0 : await connections.MarkGrantsForReview(changed, cancellationToken);
        return new CatalogChange(added, changed.Count, missing.Count, forReview);
    }

    #endregion

    #region Private Methods

    /// <summary>A tool this server will keep: it has a name that fits, and a schema that is an object of a size it accepts.</summary>
    private static bool IsStorable(ListedTool tool) =>
        tool.Name.Length is > 0 and <= MaxNameLength
        && tool.InputSchema.ValueKind is JsonValueKind.Object
        && tool.InputSchema.GetRawText().Length <= MaxSchemaLength;

    /// <summary>The hash a change of name, description, input schema, or annotations changes.</summary>
    private static string HashOf(ListedTool tool) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{tool.Name}\n{tool.Title}\n{tool.Description}\n{tool.InputSchema.GetRawText()}\n{tool.Annotations?.GetRawText()}")));

    private static string? Shorten(string? text, int length) =>
        text is null || text.Length <= length ? text : text[..length];

    #endregion
}
