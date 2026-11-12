namespace Drover.Analyzers.Tests;

/// <summary>The samples in CODE_STYLE.md are the authority: the compliant ones must pass every rule.</summary>
public sealed class CodeStyleSamplesTests
{
    #region Public Methods

    [Fact]
    public async Task Compliant_samples_in_CODE_STYLE_produce_no_diagnostics()
    {
        var samples = CodeStyleSamples.Compliant();
        var problems = new List<string>();

        foreach (var sample in samples)
        {
            var diagnostics = await Analyze(sample);
            problems.AddRange(diagnostics.Select(diagnostic => $"{diagnostic.Id} in:\n{sample}"));
        }

        samples.Count.Should().BeGreaterThan(20, "the guide has many compliant samples and all of them must be read");
        problems.Should().BeEmpty();
    }

    [Fact]
    public void Every_compliant_sample_parses_after_wrapping()
    {
        var unparsable = CodeStyleSamples.Compliant().Where(sample => ParseCleanly(sample) is null).ToList();

        unparsable.Should().BeEmpty("a sample that does not parse is not being checked");
    }

    [Fact]
    public async Task The_non_compliant_Async_sample_in_CODE_STYLE_is_caught()
    {
        var caught = new HashSet<string>();

        foreach (var sample in CodeStyleSamples.NonCompliant())
        {
            var diagnostics = await Analyze(sample);
            caught.UnionWith(diagnostics.Select(diagnostic => diagnostic.Id));
        }

        caught.Should().Contain(DiagnosticIds.NoAsyncSuffix);
    }

    #endregion

    #region Private Methods

    private static async Task<List<Diagnostic>> Analyze(string sample)
    {
        var wrapped = ParseCleanly(sample);
        return wrapped is null ? [] : await AllRules.Run([("Sample.cs", wrapped)], []);
    }

    private static string? ParseCleanly(string sample) =>
        CodeStyleSamples.Wrappings(sample).FirstOrDefault(candidate =>
            !CSharpSyntaxTree.ParseText(candidate, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
                .GetDiagnostics()
                .Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error));

    #endregion
}
