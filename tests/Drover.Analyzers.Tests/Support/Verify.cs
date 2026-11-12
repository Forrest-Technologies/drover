namespace Drover.Analyzers.Tests.Support;

/// <summary>Runs one analyzer, or one analyzer with its code fix, over a source sample.</summary>
public static class Verify
{
    #region Private Fields

    private const string DefaultAssemblyName = "Drover.Sample";

    #endregion

    #region Public Methods

    /// <summary>Markup such as {|DRV0001:Name|} marks where a diagnostic is expected. No markup expects none.</summary>
    public static Task Analyzer<TAnalyzer>(string source, string assemblyName = DefaultAssemblyName)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = new ReferenceAssemblies("net10.0"),
        };

        test.TestState.AdditionalReferences.AddRange(RuntimeReferences.All);
        test.SolutionTransforms.Add(RenameAssembly(assemblyName));
        return test.RunAsync();
    }

    /// <summary>The same, for a source file with a given name, such as GlobalUsings.cs.</summary>
    public static Task AnalyzerFile<TAnalyzer>(string fileName, string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            ReferenceAssemblies = new ReferenceAssemblies("net10.0"),
        };

        test.TestState.Sources.Add((fileName, source));
        test.TestState.AdditionalReferences.AddRange(RuntimeReferences.All);
        test.SolutionTransforms.Add(RenameAssembly(DefaultAssemblyName));
        return test.RunAsync();
    }

    public static Task CodeFix<TAnalyzer, TCodeFix>(string source, string fixedSource)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = NewCodeFixTest<TAnalyzer, TCodeFix>();
        test.TestCode = source;
        test.FixedCode = fixedSource;
        return test.RunAsync();
    }

    /// <summary>A fix that edits GlobalUsings.cs as well as the file it was offered in.</summary>
    public static Task CodeFixWithGlobalUsings<TAnalyzer, TCodeFix>(
        (string Before, string After) globalUsings,
        (string Before, string After) source)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = NewCodeFixTest<TAnalyzer, TCodeFix>();
        test.TestState.Sources.Add(("GlobalUsings.cs", globalUsings.Before));
        test.TestState.Sources.Add(("Test.cs", source.Before));
        test.FixedState.Sources.Add(("GlobalUsings.cs", globalUsings.After));
        test.FixedState.Sources.Add(("Test.cs", source.After));
        return test.RunAsync();
    }

    #endregion

    #region Private Methods

    private static CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier> NewCodeFixTest<TAnalyzer, TCodeFix>()
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            ReferenceAssemblies = new ReferenceAssemblies("net10.0"),
        };

        test.TestState.AdditionalReferences.AddRange(RuntimeReferences.All);
        test.SolutionTransforms.Add(RenameAssembly(DefaultAssemblyName));
        return test;
    }

    private static Func<Solution, ProjectId, Solution> RenameAssembly(string assemblyName) =>
        (solution, projectId) => solution.WithProjectAssemblyName(projectId, assemblyName);

    #endregion
}
