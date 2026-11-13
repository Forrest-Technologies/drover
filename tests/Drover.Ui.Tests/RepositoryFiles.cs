namespace Drover.Ui.Tests;

/// <summary>
/// Finds the interface source files, so tests can read the styles and markup that ship.
/// </summary>
public static class RepositoryFiles
{
    #region Private Fields

    private static readonly string[] interfaceProjects = ["Drover.Ui", "Drover.Web"];

    private static readonly string[] interfaceExtensions = [".razor", ".css"];

    #endregion

    #region Properties

    public static string Root { get; } = FindRoot();

    public static string TokensPath => Path.Combine(Root, "src", "Drover.Ui", "wwwroot", "tokens.css");

    #endregion

    #region Public Methods

    public static IEnumerable<string> InterfaceFiles() =>
        interfaceProjects
            .Select(project => Path.Combine(Root, "src", project))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Where(IsInterfaceSource);

    public static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    #endregion

    #region Private Methods

    private static bool IsInterfaceSource(string path)
    {
        var relative = Relative(path);
        return interfaceExtensions.Contains(Path.GetExtension(path))
            && !relative.Contains("/bin/", StringComparison.Ordinal)
            && !relative.Contains("/obj/", StringComparison.Ordinal);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Drover.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Drover.slnx was not found above the test output.");
    }

    #endregion
}
