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
    /// The model-visible name of every tool of one connection, by tool id. Tools are walked in first-seen order, ties
    /// by name, and each takes the first name in the sequence plain, six, ten, fourteen, ... that no earlier tool
    /// holds.
    /// A tool seen later can never move an earlier one, whether by accident or by a name a server chose. Names are
    /// unique by construction, and no tool is left out.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> ForConnection(string connectionSlug, IReadOnlyList<ToolName> tools)
    {
        Dictionary<Guid, string> names = [];
        HashSet<string> taken = [];

        foreach (var tool in tools
            .OrderBy(tool => tool.FirstSeenAt)
            .ThenBy(tool => tool.Name, StringComparer.Ordinal))
        {
            var name = FirstFree(connectionSlug, tool, taken);
            names[tool.Id] = name;
            taken.Add(name);
        }

        return names;
    }

    #endregion

    #region Private Methods

    private static string FirstFree(string connectionSlug, ToolName tool, HashSet<string> taken)
    {
        var plain = ForModel(connectionSlug, tool.Name);

        if (!taken.Contains(plain))
        {
            return plain;
        }

        var suffixLength = ShortSuffix;

        while (suffixLength < MaxLength)
        {
            var candidate = Suffixed(connectionSlug, tool.Name, suffixLength);

            if (!taken.Contains(candidate))
            {
                return candidate;
            }

            suffixLength += SuffixStep;
        }

        // About fifteen crafted names can take every short prefix, not only two tools with one hash. The tie-break
        // counts the names already handed out, so the same tool list gives the same name on every run.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tool.Name))).ToLowerInvariant();

        for (var index = 0; ; index++)
        {
            var suffix = $"{hash[..LongSuffix]}{index:x}";
            var candidate = WithSuffix(connectionSlug, tool.Name, suffix);

            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Suffixed(string connectionSlug, string toolName, int suffixLength)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(toolName)));
        return WithSuffix(connectionSlug, toolName, hash.ToLowerInvariant()[..suffixLength]);
    }

    private static string WithSuffix(string connectionSlug, string toolName, string suffix)
    {
        var prefix = Base(connectionSlug, toolName);
        var cut = Math.Min(prefix.Length, MaxLength - suffix.Length - 1);
        return $"{prefix[..cut]}_{suffix}";
    }

    private static string Base(string connectionSlug, string toolName) => $"{connectionSlug}__{Clean(toolName)}";

    private static string Clean(string toolName) =>
        new([.. toolName.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_')]);

    #endregion
}
