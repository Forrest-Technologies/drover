namespace Drover.Engine.Tools;

/// <summary>
/// The name a model sees for a tool server's tool: the connection's short name, two underscores, and the tool's own
/// name. The short name is unique in the space and has no double underscore, so the prefix cannot be forged by a
/// tool name, and a server cannot give a tool the name of a built-in or of another server's tool.
/// </summary>
public static class ToolNames
{
    #region Private Fields

    /// <summary>The longest tool name every model provider accepts.</summary>
    private const int MaxLength = 64;

    private const int ShortSuffix = 6;

    private const int LongSuffix = 10;

    #endregion

    #region Public Methods

    public static string ForModel(string connectionSlug, string toolName)
    {
        var name = Base(connectionSlug, toolName);
        return name.Length <= MaxLength ? name : name[..MaxLength];
    }

    /// <summary>
    /// The model-visible name of every tool of one connection, by tool id. Tools whose cleaned names clash keep the
    /// base name for the one first seen; the others get a suffix made from their own name, lengthened when two short
    /// suffixes still clash. A tool that works today keeps its name.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> ForConnection(string connectionSlug, IReadOnlyList<ToolName> tools)
    {
        Dictionary<Guid, string> baseNames = [];

        foreach (var tool in tools)
        {
            baseNames[tool.Id] = ForModel(connectionSlug, tool.Name);
        }

        var shared = baseNames.Values
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<Guid, string> names = [];
        HashSet<Guid> suffixed = [];

        foreach (var tool in tools)
        {
            var baseName = baseNames[tool.Id];

            if (shared.Contains(baseName) && FirstSeen(tools, baseNames, baseName).Id != tool.Id)
            {
                names[tool.Id] = Suffixed(connectionSlug, tool.Name, ShortSuffix);
                suffixed.Add(tool.Id);
            }
            else
            {
                names[tool.Id] = baseName;
            }
        }

        // Two short suffixes from two different names can still be the same. Those tools take a longer one; a base
        // name never changes, so a tool that already works keeps it whatever another tool's hash does.
        var stillShared = names.Values
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var tool in tools.Where(tool => suffixed.Contains(tool.Id) && stillShared.Contains(names[tool.Id])))
        {
            names[tool.Id] = Suffixed(connectionSlug, tool.Name, LongSuffix);
        }

        return names;
    }

    #endregion

    #region Private Methods

    private static ToolName FirstSeen(IReadOnlyList<ToolName> tools, Dictionary<Guid, string> baseNames, string baseName) =>
        tools
            .Where(tool => baseNames[tool.Id] == baseName)
            .OrderBy(tool => tool.FirstSeenAt)
            .ThenBy(tool => tool.Name, StringComparer.Ordinal)
            .First();

    private static string Suffixed(string connectionSlug, string toolName, int suffixLength)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(toolName)));
        var suffix = hash.ToLowerInvariant()[..suffixLength];
        var prefix = Base(connectionSlug, toolName);
        var cut = Math.Min(prefix.Length, MaxLength - suffix.Length - 1);
        return $"{prefix[..cut]}_{suffix}";
    }

    private static string Base(string connectionSlug, string toolName) => $"{connectionSlug}__{Clean(toolName)}";

    private static string Clean(string toolName) =>
        new([.. toolName.Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_')]);

    #endregion
}
