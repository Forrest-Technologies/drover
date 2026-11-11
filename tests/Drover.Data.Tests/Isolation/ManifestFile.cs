namespace Drover.Data.Tests.Isolation;

/// <summary>One line of <c>GlobalTables.manifest</c>: a table, and the entity class mapped to it when there is one.</summary>
public sealed record GlobalTableEntry(string Table, string? Entity);

/// <summary>Reads the two manifest files. The project file copies both next to the test assembly.</summary>
public static class ManifestFile
{
    #region Public Methods

    public static IReadOnlyList<GlobalTableEntry> ReadGlobalTables() =>
        [.. Words("GlobalTables.manifest").Select(words => new GlobalTableEntry(words[0], words.ElementAtOrDefault(1)))];

    /// <summary>Function signatures as <c>schema.name(argument types)</c>, with the spaces taken out.</summary>
    public static IReadOnlyList<string> ReadSystemFunctions() =>
        [.. Words("SystemFunctions.manifest").Select(words => Normalize(string.Join(string.Empty, words)))];

    public static string Normalize(string signature) => signature.Replace(" ", string.Empty);

    #endregion

    #region Private Methods

    private static IEnumerable<string[]> Words(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);

        foreach (var line in File.ReadAllLines(path))
        {
            var content = line.Split('#')[0];

            if (!string.IsNullOrWhiteSpace(content))
            {
                yield return content.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            }
        }
    }

    #endregion
}
