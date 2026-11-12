namespace Drover.Analyzers.Tests;

/// <summary>
/// The analyzer projects are not given to their own analyzers by the build (a project cannot be analyzed by the
/// assembly it is still compiling), so this runs every rule over their source instead.
/// </summary>
public sealed class AnalyzersObeyTheirOwnRulesTests
{
    #region Public Methods

    [Theory]
    [InlineData("tools/Drover.Analyzers")]
    [InlineData("tools/Drover.Analyzers.CodeFixes")]
    public async Task Analyzer_projects_produce_no_Drover_diagnostics_on_their_own_source(string project)
    {
        var files = RepositoryFiles.SourceFiles(project).Select(path => (path, File.ReadAllText(path))).ToList();

        var diagnostics = await AllRules.Run(files, RoslynReferences(), assemblyName: Path.GetFileName(project));

        files.Should().NotBeEmpty();
        diagnostics.Select(Describe).Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static IEnumerable<MetadataReference> RoslynReferences() =>
        new[]
        {
            typeof(Compilation).Assembly,
            typeof(CSharpCompilation).Assembly,
            typeof(Document).Assembly,
            typeof(System.Composition.SharedAttribute).Assembly,
            typeof(DiagnosticIds).Assembly,
        }.Select(assembly => MetadataReference.CreateFromFile(assembly.Location));

    private static string Describe(Diagnostic diagnostic) =>
        $"{diagnostic.Id} {diagnostic.Location.GetLineSpan()}: {diagnostic.GetMessage()}";

    #endregion
}
