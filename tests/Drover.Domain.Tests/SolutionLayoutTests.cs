namespace Drover.Domain.Tests;

/// <summary>
/// Holds the dependency direction in docs/architecture/01-solution-layout.md.
/// A new project reference fails here until this table says it is allowed.
/// </summary>
public sealed class SolutionLayoutTests
{
    #region Private Fields

    private static readonly Dictionary<string, string[]> allowedReferences = new()
    {
        ["Drover.Contracts"] = [],
        ["Drover.Domain"] = ["Drover.Contracts"],
        ["Drover.Data"] = ["Drover.Domain"],
        ["Drover.Engine"] = ["Drover.Data", "Drover.Domain", "Drover.Contracts"],
        ["Drover.Agents"] = ["Drover.Data", "Drover.Domain"],
        ["Drover.Adapters.OpenAICompatible"] = ["Drover.Domain"],
        ["Drover.Adapters.Venice"] = ["Drover.Domain"],
        ["Drover.Adapters.Mcp"] = ["Drover.Domain"],
        ["Drover.Adapters.LocalFiles"] = ["Drover.Domain"],
        ["Drover.Adapters.DataProtection"] = ["Drover.Domain", "Drover.Data"],
        ["Drover.Client"] = ["Drover.Contracts"],
        ["Drover.Ui"] = ["Drover.Client", "Drover.Contracts"],
        ["Drover.Web"] = ["Drover.Ui"],
        ["Drover.Server"] =
        [
            "Drover.Engine",
            "Drover.Agents",
            "Drover.Adapters.OpenAICompatible",
            "Drover.Adapters.Venice",
            "Drover.Adapters.Mcp",
            "Drover.Adapters.LocalFiles",
            "Drover.Adapters.DataProtection",
            "Drover.Web",
        ],
    };

    private static readonly string[] clientProjects = ["Drover.Client", "Drover.Ui", "Drover.Web"];

    private static readonly string[] serverOnlyProjects = ["Drover.Domain", "Drover.Data", "Drover.Engine"];

    #endregion

    #region Public Methods

    [Fact]
    public void Every_source_project_is_in_the_layout_table()
    {
        var projects = FindSourceProjects().Select(ProjectName);

        projects.Should().BeEquivalentTo(allowedReferences.Keys);
    }

    [Fact]
    public void Project_references_follow_the_dependency_direction()
    {
        foreach (var project in FindSourceProjects())
        {
            var name = ProjectName(project);

            ReadProjectReferences(project).Should().BeSubsetOf(
                allowedReferences[name],
                $"{name} may only reference what docs/architecture/01-solution-layout.md allows");
        }
    }

    [Fact]
    public void Domain_never_references_model_vendor_packages_even_when_the_code_does_not_use_them()
    {
        var references = XDocument.Load(FindSourceProject("Drover.Domain"))
            .Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty);
        references.Should().NotContain(reference => reference.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal)
            || reference.StartsWith("OpenAI", StringComparison.Ordinal)
            || reference.StartsWith("Microsoft.Agents", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_the_compatible_protocol_adapter_references_the_openai_sdk()
    {
        var projects = FindSourceProjects().Where(project => XDocument.Load(project)
            .Descendants("PackageReference")
            .Any(reference => reference.Attribute("Include")?.Value is "OpenAI" or "Microsoft.Extensions.AI.OpenAI"));
        projects.Select(ProjectName).Should().Equal("Drover.Adapters.OpenAICompatible");
    }

    [Fact]
    public void Engine_never_references_an_adapter_or_the_agent_runtime()
    {
        var references = ReadProjectReferences(FindSourceProject("Drover.Engine"));

        references.Should().NotContain(reference =>
            reference.StartsWith("Drover.Adapters.", StringComparison.Ordinal) || reference == "Drover.Agents");
    }

    [Fact]
    public void Clients_never_reference_server_side_projects()
    {
        foreach (var client in clientProjects)
        {
            ReadProjectReferences(FindSourceProject(client)).Should().NotIntersectWith(serverOnlyProjects);
        }
    }

    [Fact]
    public void Only_the_solution_format_slnx_is_present()
    {
        Directory.GetFiles(FindRepositoryRoot(), "*.sln").Should().BeEmpty();
    }

    #endregion

    #region Repository Lookup

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Drover.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Drover.slnx was not found above the test directory.");
    }

    private static string[] FindSourceProjects() =>
        Directory.GetFiles(Path.Combine(FindRepositoryRoot(), "src"), "*.csproj", SearchOption.AllDirectories);

    private static string FindSourceProject(string name) =>
        FindSourceProjects().Single(project => ProjectName(project) == name);

    private static string ProjectName(string projectPath) => Path.GetFileNameWithoutExtension(projectPath);

    private static List<string> ReadProjectReferences(string projectPath) =>
    [
        .. XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/')))
    ];

    #endregion
}
