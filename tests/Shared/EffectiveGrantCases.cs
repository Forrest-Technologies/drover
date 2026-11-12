namespace Drover.Tests.Common;

/// <summary>One line of <c>effective-grants.cases</c>. See that file for the format.</summary>
public sealed record GrantCase(string Name, IReadOnlyList<string> Path, string Expected, bool OwnClosureRow = true)
{
    #region Public Methods

    /// <summary>The grants along the path as the rule takes them, the bot first. A missing grant is null.</summary>
    public IReadOnlyList<Drover.Domain.Permissions.GrantEntry?> Entries() => [.. Path.Select(ParseEntry)];

    public Drover.Contracts.Permissions.GrantEffect? ExpectedEffect() => ParseEffect(Expected);

    public static Drover.Contracts.Permissions.GrantEffect? ParseEffect(string text) =>
        text == "none" ? null : Enum.Parse<Drover.Contracts.Permissions.GrantEffect>(text, ignoreCase: true);

    #endregion

    #region Private Methods

    private static Drover.Domain.Permissions.GrantEntry? ParseEntry(string token)
    {
        if (token == "none")
        {
            return null;
        }

        var parts = token.Split(':');
        var review = parts.Length > 1 && parts[1] == "review"
            ? Drover.Contracts.Permissions.ReviewState.NeedsReview
            : Drover.Contracts.Permissions.ReviewState.Current;
        return new Drover.Domain.Permissions.GrantEntry(ParseEffect(parts[0])!.Value, review);
    }

    #endregion
}

/// <summary>Reads <c>effective-grants.cases</c>, which both test projects copy next to their assembly.</summary>
public static class EffectiveGrantCases
{
    #region Public Methods

    public static IReadOnlyList<GrantCase> Load()
    {
        var cases = new List<GrantCase>();

        foreach (var line in File.ReadAllLines(System.IO.Path.Combine(AppContext.BaseDirectory, "effective-grants.cases")))
        {
            var fields = line.Split('|', StringSplitOptions.TrimEntries);

            if (!line.TrimStart().StartsWith('#') && fields.Length is 3 or 4)
            {
                cases.Add(new GrantCase(
                    fields[0],
                    [.. fields[1].Split(',', StringSplitOptions.TrimEntries)],
                    fields[2],
                    OwnClosureRow: fields.Length < 4 || fields[3] != "no-own-row"));
            }
        }

        return cases;
    }

    public static GrantCase Find(string name) => Load().Single(found => found.Name == name);

    /// <summary>The case names, for a theory. Xunit shows each as its own test.</summary>
    public static TheoryData<string> Names() => [.. Load().Select(found => found.Name)];

    #endregion
}
