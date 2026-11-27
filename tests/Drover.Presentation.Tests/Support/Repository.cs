namespace Drover.Presentation.Tests.Support;

/// <summary>Finds the client's source files, so tests can read what ships.</summary>
public static class Repository
{
    #region Properties

    public static string Root { get; } = FindRoot();

    public static string NativeFolder => Path.Combine(Root, "src", "Drover.Native");

    /// <summary>The view models and words the native client draws from.</summary>
    public static string PresentationFolder => Path.Combine(Root, "src", "Drover.Presentation");

    /// <summary>The design tokens as CSS custom properties: the specification <c>Tokens.xaml</c> restates.</summary>
    public static string TokenSpecification => Path.Combine(NativeFolder, "Theme", "tokens.css");

    /// <summary>The theme files, one JSON file per theme: the source of every look.</summary>
    public static string ThemeFolder => Path.Combine(Root, "src", "Drover.Presentation", "Theme", "Themes");

    public static string NativeTokens => Path.Combine(NativeFolder, "Theme", "Tokens.xaml");

    /// <summary>
    /// The names of the kinds of model provider the server builds: each adapter's <c>ConnectionKind</c> constant and
    /// the constants of <c>ModelConnectionKinds</c>, read from the source so a new adapter is counted without an edit here.
    /// </summary>
    public static IReadOnlyList<string> ProviderKinds { get; } = ReadProviderKinds();

    #endregion

    #region Public Methods

    /// <summary>The native client's own source files with one of the extensions, as paths from its folder.</summary>
    public static IEnumerable<(string Path, string Text)> NativeFiles(params string[] extensions) =>
        SourceFiles(NativeFolder, extensions);

    /// <summary>The presentation's own source files with one of the extensions, as paths from its folder.</summary>
    public static IEnumerable<(string Path, string Text)> PresentationFiles(params string[] extensions) =>
        SourceFiles(PresentationFolder, extensions);

    #endregion

    #region Private Methods

    private static IEnumerable<(string Path, string Text)> SourceFiles(string folder, params string[] extensions) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(path => (Full: path, Relative: System.IO.Path.GetRelativePath(folder, path).Replace('\\', '/')))
            .Where(file => !file.Relative.StartsWith("bin/", StringComparison.Ordinal) && !file.Relative.StartsWith("obj/", StringComparison.Ordinal))
            .Where(file => extensions.Contains(System.IO.Path.GetExtension(file.Full)))
            .Select(file => (file.Relative, File.ReadAllText(file.Full)));

    private static List<string> ReadProviderKinds()
    {
        var adapters = Directory.EnumerateDirectories(Path.Combine(Root, "src"), "Drover.Adapters.*")
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.TopDirectoryOnly))
            .SelectMany(file => System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(file), "ConnectionKind\\s*=\\s*\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value));
        var shared = System.Text.RegularExpressions.Regex
            .Matches(File.ReadAllText(Path.Combine(Root, "src", "Drover.Domain", "Models", "ModelConnectionKinds.cs")), "const string \\w+ = \"([^\"]+)\"")
            .Select(match => match.Groups[1].Value);

        return [.. adapters.Concat(shared).Distinct()];
    }

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
