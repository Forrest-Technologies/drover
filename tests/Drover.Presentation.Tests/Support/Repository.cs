namespace Drover.Presentation.Tests.Support;

/// <summary>Finds the source files of the two clients, so tests can read what ships.</summary>
public static class Repository
{
    #region Properties

    public static string Root { get; } = FindRoot();

    public static string WebTokens => Path.Combine(Root, "src", "Drover.Ui", "wwwroot", "tokens.css");

    public static string NativeFolder => Path.Combine(Root, "src", "Drover.Native");

    public static string NativeTokens => Path.Combine(NativeFolder, "Theme", "Tokens.xaml");

    #endregion

    #region Public Methods

    /// <summary>The native client's own source files with one of the extensions, as paths from its folder.</summary>
    public static IEnumerable<(string Path, string Text)> NativeFiles(params string[] extensions) =>
        Directory.EnumerateFiles(NativeFolder, "*", SearchOption.AllDirectories)
            .Select(path => (Full: path, Relative: System.IO.Path.GetRelativePath(NativeFolder, path).Replace('\\', '/')))
            .Where(file => !file.Relative.StartsWith("bin/", StringComparison.Ordinal) && !file.Relative.StartsWith("obj/", StringComparison.Ordinal))
            .Where(file => extensions.Contains(System.IO.Path.GetExtension(file.Full)))
            .Select(file => (file.Relative, File.ReadAllText(file.Full)));

    #endregion

    #region Private Methods

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Drover.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Drover.slnx was not found above the test directory.");
    }

    #endregion
}
