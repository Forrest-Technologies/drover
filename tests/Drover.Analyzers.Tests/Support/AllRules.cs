namespace Drover.Analyzers.Tests.Support;

/// <summary>Runs every Drover rule over source text and reports the Drover diagnostics.</summary>
public static class AllRules
{
    #region Private Fields

    private static readonly ImmutableArray<DiagnosticAnalyzer> analyzers =
    [
        new NoAsyncSuffixAnalyzer(),
        new GlobalUsingsOnlyAnalyzer(),
        new RegionsRequiredAnalyzer(),
        new NoStringConcatenationAnalyzer(),
        new NoSilentCatchAnalyzer(),
        new DataAccessContainmentAnalyzer(),
    ];

    #endregion

    #region Public Methods

    public static async Task<List<Diagnostic>> Run(
        IEnumerable<(string Path, string Text)> files,
        IEnumerable<MetadataReference> extraReferences,
        string assemblyName = "Drover.Sample")
    {
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var trees = files.Select(file => CSharpSyntaxTree.ParseText(file.Text, options, file.Path)).ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            [.. RuntimeReferences.All, .. extraReferences],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return [.. diagnostics.Where(diagnostic => diagnostic.Id.StartsWith("DRV", StringComparison.Ordinal))];
    }

    #endregion
}
