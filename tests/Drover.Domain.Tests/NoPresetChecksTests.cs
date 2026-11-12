namespace Drover.Domain.Tests;

/// <summary>
/// Feature code asks the policy a specific question and never asks which preset is selected. Only the policy
/// types, the code that loads the policy, and the status listing may name a preset. Any other source file in
/// <c>src</c> that names one, compares against one, or reads the preset's name fails here.
/// </summary>
public sealed partial class NoPresetChecksTests
{
    #region Private Fields

    private static readonly string[] allowedPaths =
    [
        "src/Drover.Domain/Policy/",
        "src/Drover.Engine/Policy/PolicyService.cs",
        "src/Drover.Server/Policy/",
    ];

    #endregion

    #region Public Methods

    [Fact]
    public void No_source_file_outside_the_policy_types_names_or_compares_a_preset()
    {
        var root = FindRepositoryRoot();

        var offenders = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(path => !allowedPaths.Any(allowed => path.StartsWith(allowed, StringComparison.Ordinal)))
            .Where(path => !path.Contains("/Migrations/", StringComparison.Ordinal))
            .Where(path => NamesAPreset(File.ReadAllText(Path.Combine(root, path))))
            .ToList();

        offenders.Should().BeEmpty("a rule is read from DeploymentPolicy or PolicyService, never derived from the preset name");
    }

    [Theory]
    [InlineData("if (options.Value.Preset == \"hosted\") { }")]
    [InlineData("if (policy.Preset is DeploymentPolicy.HostedName) { }")]
    [InlineData("var strict = policy == DeploymentPolicy.Hosted;")]
    [InlineData("var name = configuration[\"DROVER_POLICY\"];")]
    [InlineData("return preset == \"self_hosted\";")]
    public void The_check_would_catch_a_comparison_against_a_preset(string source)
    {
        NamesAPreset(source).Should().BeTrue();
    }

    [Theory]
    [InlineData("if (policy.Deployment.AllowPrivateAddresses) { }")]
    [InlineData("var hosted = false;")]
    [InlineData("// the preset is chosen at start")]
    public void The_check_leaves_alone_code_that_asks_a_specific_question(string source)
    {
        NamesAPreset(source).Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private static bool NamesAPreset(string source) => PresetPattern().IsMatch(source);

    [GeneratedRegex("""\b(SelfHostedName|HostedName)\b|DeploymentPolicy\.(SelfHosted|Hosted)\b|"(self_hosted|hosted)"|\.Preset\b|\bDROVER_POLICY\b""")]
    private static partial Regex PresetPattern();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Drover.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Drover.slnx was not found above the test directory.");
    }

    #endregion
}
