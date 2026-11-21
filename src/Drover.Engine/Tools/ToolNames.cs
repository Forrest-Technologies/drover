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

    private const int SuffixStep = 4;

    #endregion

    #region Public Methods

    public static string ForModel(string connectionSlug, string toolName)
    {
        var name = Base(connectionSlug, toolName);
        return name.Length <= MaxLength ? name : name[..MaxLength];
    }

    /// <summary>
    /// The model-visible name of every tool of one connection, by tool id. Each plain name belongs to the tool that
    /// had it alone or saw it first; every other tool takes the shortest suffix from its own name that no other
    /// tool holds. A server-named tool can aim at another tool's suffix, so candidates are checked against all plain
    /// names as well as the ones already given out. Names are unique by construction, and no tool is left out.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> ForConnection(string connectionSlug, IReadOnlyList<ToolName> tools)
    {
        Dictionary<Guid, string> baseNames = [];

        foreach (var tool in tools)
        {
            baseNames[tool.Id] = ForModel(connectionSlug, tool.Name);
        }

        Dictionary<Guid, string> names = [];
        HashSet<string> taken = [];
        var ordered = tools
            .OrderBy(tool => tool.FirstSeenAt)
            .ThenBy(tool => tool.Name, StringComparer.Ordinal)
            .ToList();

        // The plain names go first: a tool that already works today keeps its name whatever another server lists.
        foreach (var group in ordered.GroupBy(tool => baseNames[tool.Id], StringComparer.Ordinal))
        {
            var keeper = group.First();
            names[keeper.Id] = baseNames[keeper.Id];
            taken.Add(baseNames[keeper.Id]);
        }

        Dictionary<Guid, int> starting = [];

        // Two tools whose six-character suffixes are the same both take a longer one, as the catalog rule says.
        foreach (var group in ordered
            .Where(tool => !names.ContainsKey(tool.Id))
            .GroupBy(tool => Suffixed(connectionSlug, tool.Name, ShortSuffix), StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            foreach (var tool in group)
            {
                starting[tool.Id] = LongSuffix;
            }
        }

        foreach (var tool in ordered.Where(tool => !names.ContainsKey(tool.Id)))
        {
            names[tool.Id] = FreeName(connectionSlug, tool, starting.GetValueOrDefault(tool.Id, ShortSuffix), taken);
        }

        return names;
    }

    #endregion

    #region Private Methods

    private static string FreeName(string connectionSlug, ToolName tool, int length, HashSet<string> taken)
    {
        for (; length < MaxLength; length += SuffixStep)
        {
            var candidate = Suffixed(connectionSlug, tool.Name, length);

            if (taken.Add(candidate))
            {
                return candidate;
            }
        }

        // Only reachable if two tools of one connection have the very same name, which the catalog does not allow.
        // The tie-break is the order the tools were given, so a restart sees the same name and neither is dropped.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tool.Name))).ToLowerInvariant();

        for (var index = 0; ; index++)
        {
            var suffix = $"{hash[..LongSuffix]}{index:x}";
            var prefix = Base(connectionSlug, tool.Name);
            var cut = Math.Min(prefix.Length, MaxLength - suffix.Length - 1);
            var candidate = $"{prefix[..cut]}_{suffix}";

            if (taken.Add(candidate))
            {
                return candidate;
            }
        }
    }

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
