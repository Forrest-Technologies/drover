namespace Drover.Ui.Tests.Starters;

/// <summary>
/// Keeps the starters honest: each one is claimed by an issue, and product code never leans on one. A starter is
/// moved into the product, not referenced from it, so the folder empties as the issues are built.
/// </summary>
public sealed partial class StarterRulesTests
{
    #region Private Fields

    private static readonly string startersFolder = Path.Combine(RepositoryFiles.Root, "src", "Drover.Ui", "Starters");

    private static readonly string[] mayNameStarters =
    [
        "src/Drover.Ui/Starters/",
        "src/Drover.Ui/Pages/Dev/StarterGallery.razor",
    ];

    #endregion

    #region Public Methods

    [Fact]
    public void Every_starter_file_is_claimed_by_an_issue_in_the_readme_and_every_claimed_file_exists()
    {
        var onDisk = Directory.EnumerateFiles(startersFolder)
            .Select(Path.GetFileName)
            .Where(name => name is not "README.md" && !name!.EndsWith(".razor.css", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
        var claimed = FileName().Matches(File.ReadAllText(Path.Combine(startersFolder, "README.md")))
            .Select(match => match.Groups["name"].Value)
            .Distinct()
            .Order(StringComparer.Ordinal);

        claimed.Should().Equal(onDisk, "the table in Starters/README.md is how an issue finds its files and how a finished one is cleaned up");
    }

    [Fact]
    public void Product_code_does_not_reference_a_starter()
    {
        var leaning =
            from path in Directory.EnumerateFiles(Path.Combine(RepositoryFiles.Root, "src"), "*", SearchOption.AllDirectories)
            where Path.GetExtension(path) is ".cs" or ".razor"
            let relative = RepositoryFiles.Relative(path)
            where !relative.Contains("/bin/", StringComparison.Ordinal) && !relative.Contains("/obj/", StringComparison.Ordinal)
            where !mayNameStarters.Any(allowed => relative.StartsWith(allowed, StringComparison.Ordinal))
            where File.ReadAllText(path).Contains("Drover.Ui.Starters", StringComparison.Ordinal)
            select relative;

        leaning.Should().BeEmpty("a starter is moved to its real home and renamed, never used from where it sits");
    }

    #endregion

    #region Private Methods

    [GeneratedRegex(@"`(?<name>[A-Za-z]+(?:\.razor(?:\.js)?|\.cs))`")]
    private static partial Regex FileName();

    #endregion
}
