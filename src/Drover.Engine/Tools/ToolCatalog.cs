namespace Drover.Engine.Tools;

/// <summary>
/// Keeps a space's catalog rows for the built-in tools in step with what this server ships. A grant always points at
/// a catalog row, so built-in tools are rows like any other. Runs in the tenant scope the caller opened.
/// </summary>
public sealed class ToolCatalog(ToolRepository tools, IEnumerable<BuiltinTool> builtins, TimeProvider time)
{
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

    #endregion
}
