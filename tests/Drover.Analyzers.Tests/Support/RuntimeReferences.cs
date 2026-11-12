namespace Drover.Analyzers.Tests.Support;

/// <summary>
/// The references test samples compile against: the runtime this test process is already running on, so no
/// reference package is downloaded and the tests stay offline.
/// </summary>
public static class RuntimeReferences
{
    #region Private Fields

    private static readonly Lazy<ImmutableArray<MetadataReference>> loaded = new(Load);

    #endregion

    #region Properties

    public static ImmutableArray<MetadataReference> All => loaded.Value;

    #endregion

    #region Private Methods

    private static ImmutableArray<MetadataReference> Load()
    {
        var platformAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;

        return
        [
            ..platformAssemblies
                .Split(Path.PathSeparator)
                .Where(IsFrameworkAssembly)
                .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)),
        ];
    }

    private static bool IsFrameworkAssembly(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith("System.", StringComparison.Ordinal) || name is "System" or "netstandard" or "mscorlib";
    }

    #endregion
}
