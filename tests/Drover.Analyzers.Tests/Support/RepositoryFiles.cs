namespace Drover.Analyzers.Tests.Support;

/// <summary>Finds files in the repository checkout the tests run from.</summary>
public static class RepositoryFiles
{
    #region Public Methods

    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Drover.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Drover.slnx was not found above the test output");
    }

    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(Root(), relativePath));

    /// <summary>Every source file of a project, leaving out build output.</summary>
    public static IEnumerable<string> SourceFiles(string projectDirectory) =>
        Directory
            .EnumerateFiles(Path.Combine(Root(), projectDirectory), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));

    #endregion

    #region Private Methods

    private static bool IsBuildOutput(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar);
        return segments.Contains("obj") || segments.Contains("bin");
    }

    #endregion
}
