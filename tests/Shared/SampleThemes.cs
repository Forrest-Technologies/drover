namespace Drover.Tests.Common;

/// <summary>
/// Sample theme files for tests: Night Yard's file under another id and name, so it passes every theme check and
/// takes none of Drover's own ids. Read from the repository, as the theme tests read theirs.
/// </summary>
public static class SampleThemes
{
    #region Public Methods

    /// <summary>A theme file's text that passes every check, under the id and name given.</summary>
    public static string File(string id = "sample-dusk", string name = "Sample dusk")
    {
        var theme = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(Path.Combine(FindRoot(), "src", "Drover.Presentation", "Theme", "Themes", "night-yard.json")))!.AsObject();
        theme["id"] = id;
        theme["name"] = name;
        return theme.ToJsonString();
    }

    /// <summary>The sample theme with its text color made the color of its background, which no theme check lets through.</summary>
    public static string Unreadable(string id = "sample-fog")
    {
        var theme = System.Text.Json.Nodes.JsonNode.Parse(File(id, "Sample fog"))!.AsObject();
        var dark = theme["dark"]!["color"]!.AsObject();
        dark["text"] = dark["bg-0"]!.GetValue<string>();
        return theme.ToJsonString();
    }

    #endregion

    #region Private Methods

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Drover.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Drover.slnx was not found above the test output.");
    }

    #endregion
}
