namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// A field the web client limits is limited in the native client too, and both read the number from the constant the
/// server's validators read (<see cref="FieldLimits"/>, and <see cref="ExtensionFile.MaxIdLength"/> for an id), so
/// no client stops a person short of what the server takes or lets them type what it refuses.
/// </summary>
public sealed partial class FieldLimitsTests
{
    #region Public Methods

    [Fact]
    public void Every_limit_on_a_web_field_reads_a_shared_constant()
    {
        var limits = WebLimits().ToList();

        limits.Should().NotBeEmpty();
        limits.Should().AllSatisfy(limit => limit.Value.Should().MatchRegex(SharedConstant().ToString(), $"{limit.Path} sets a length of its own"));
    }

    [Fact]
    public void Every_limit_on_a_native_field_reads_a_shared_constant()
    {
        var literals = Repository.NativeFiles(".cs", ".xaml")
            .SelectMany(file => NativeLiteralLimit().Matches(file.Text).Select(match => $"{file.Path}: {match.Value}"));

        literals.Should().BeEmpty();
    }

    [Fact]
    public void Every_shared_limit_the_web_client_sets_is_set_as_often_in_the_native_client()
    {
        var native = Repository.NativeFiles(".cs", ".xaml")
            .SelectMany(file => SharedConstant().Matches(file.Text).Select(match => match.Value))
            .CountBy(name => name)
            .ToDictionary();

        var missing = WebLimits()
            .Select(limit => SharedConstant().Match(limit.Value).Value)
            .CountBy(name => name)
            .Where(web => native.GetValueOrDefault(web.Key) < web.Value)
            .Select(web => $"{web.Key}: {web.Value} on the web, {native.GetValueOrDefault(web.Key)} in the native client");

        missing.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static IEnumerable<(string Path, string Value)> WebLimits()
    {
        var folder = Path.Combine(Repository.Root, "src", "Drover.Ui");

        return Directory.EnumerateFiles(folder, "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Starters{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => WebLimit().Matches(File.ReadAllText(path))
                .Select(match => (Path.GetRelativePath(folder, path), match.Groups["value"].Value)));
    }

    [GeneratedRegex("""maxlength="(?<value>[^"]*)"\s*""", RegexOptions.IgnoreCase)]
    private static partial Regex WebLimit();

    [GeneratedRegex(@"\b(FieldLimits\.\w+|ExtensionFile\.MaxIdLength)\b")]
    private static partial Regex SharedConstant();

    [GeneratedRegex(@"MaxLength\s*=\s*""?\d")]
    private static partial Regex NativeLiteralLimit();

    #endregion
}
