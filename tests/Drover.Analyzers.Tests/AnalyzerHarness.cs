namespace Drover.Analyzers.Tests;

/// <summary>Compiles a sample, runs one analyzer over it, and returns what it reported.</summary>
public static class AnalyzerHarness
{
    #region Private Fields

    /// <summary>The few types the analyzer looks for, declared in the sample so no EF Core reference is needed.</summary>
    private const string Stubs =
        """
        namespace Microsoft.EntityFrameworkCore
        {
            public class DbContext { }
            public class DbSet<TEntity> where TEntity : class { }
        }

        namespace Drover.Domain.Tenancy
        {
            public interface ISpaceOwned { System.Guid SpaceId { get; set; } }
            public interface IUserOwned { System.Guid UserId { get; set; } }
        }
        """;

    private static readonly string[] referenceNames = ["System.Runtime", "System.Private.CoreLib"];

    #endregion

    #region Public Methods

    public static async Task<ImmutableArray<Diagnostic>> Run(
        DiagnosticAnalyzer analyzer,
        string source,
        string? manifest = null)
    {
        var compilation = CSharpCompilation.Create(
            "Sample",
            [CSharpSyntaxTree.ParseText(Stubs), CSharpSyntaxTree.ParseText(source)],
            LoadReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<AdditionalText> files = manifest is null
            ? []
            : [new InMemoryText("GlobalTables.manifest", manifest)];

        var withAnalyzers = compilation.WithAnalyzers([analyzer], new AnalyzerOptions(files));
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    }

    #endregion

    #region Private Methods

    private static IEnumerable<MetadataReference> LoadReferences()
    {
        var platform = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return platform
            .Split(Path.PathSeparator)
            .Where(path => referenceNames.Contains(Path.GetFileNameWithoutExtension(path)))
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    #endregion

    #region Nested Types

    private sealed class InMemoryText(string path, string text) : AdditionalText
    {
        #region Properties

        public override string Path { get; } = path;

        #endregion

        #region Public Methods

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);

        #endregion
    }

    #endregion
}
