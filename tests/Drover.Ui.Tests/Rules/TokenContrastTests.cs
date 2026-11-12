namespace Drover.Ui.Tests.Rules;

/// <summary>
/// Recomputes the contrast DESIGN.md promises, from the values in tokens.css, for both themes.
/// </summary>
public sealed class TokenContrastTests
{
    #region Private Fields

    private const double TextMinimum = 4.5;

    private const double BorderMinimum = 3;

    private const string ControlBorder = "--dv-text-3";

    private const string SystemLightQuery = "prefers-color-scheme: light";

    private const string LightSelector = """:root[data-theme="light"]""";

    private static readonly string[] surfaces = ["--dv-bg-0", "--dv-bg-1", "--dv-bg-2", "--dv-bg-3"];

    private static readonly string[] textAndStatus =
    [
        "--dv-text",
        "--dv-text-2",
        "--dv-text-3",
        "--dv-ember",
        "--dv-ion",
        "--dv-violet",
        "--dv-ok",
        "--dv-warn",
        "--dv-danger",
        .. Enumerable.Range(0, 12).Select(step => $"--dv-hue-{step}"),
    ];

    private static readonly IReadOnlyList<StyleRule> rules =
        StyleSheet.Parse(File.ReadAllText(RepositoryFiles.TokensPath));

    private static readonly Dictionary<string, string> dark =
        Declared(rule => rule.Selector is ":root" && rule.Context.Count == 0);

    private static readonly Dictionary<string, string> explicitLight =
        Declared(rule => rule.Selector == LightSelector && rule.Context.Count == 0);

    private static readonly Dictionary<string, string> systemLight =
        Declared(rule => rule.Context.Any(outer => outer.Contains(SystemLightQuery, StringComparison.Ordinal)));

    private static readonly Dictionary<string, Dictionary<string, string>> themes = new()
    {
        ["dark"] = dark,
        ["light"] = dark.Concat(explicitLight)
            .GroupBy(pair => pair.Key)
            .ToDictionary(group => group.Key, group => group.Last().Value),
    };

    #endregion

    #region Public Methods

    [Fact]
    public void Contrast_is_computed_the_way_the_guideline_defines_it()
    {
        Contrast.Ratio("#FFFFFF", "#000000").Should().BeApproximately(21, 0.001);
        Contrast.Ratio("#000000", "#FFFFFF").Should().BeApproximately(21, 0.001);
        Contrast.Ratio("#777777", "#FFFFFF").Should().BeApproximately(4.478, 0.001);
    }

    [Fact]
    public void Every_text_and_status_color_clears_4_5_to_1_on_every_surface_in_both_themes() =>
        AssertNone(Failures(textAndStatus, surfaces, TextMinimum));

    [Fact]
    public void The_control_border_clears_3_to_1_on_every_surface_in_both_themes() =>
        AssertNone(Failures([ControlBorder], surfaces, BorderMinimum));

    [Fact]
    public void Text_on_the_accent_clears_4_5_to_1_in_both_themes() =>
        AssertNone(Failures(["--dv-on-accent"], ["--dv-ember"], TextMinimum));

    [Fact]
    public void The_light_theme_holds_every_color_the_dark_theme_does()
    {
        var colors = dark.Keys.Where(name => dark[name].StartsWith('#'));

        explicitLight.Keys.Should().Contain(colors);
    }

    [Fact]
    public void The_system_light_block_holds_the_same_values_as_the_chosen_light_theme()
    {
        explicitLight.Should().NotBeEmpty();
        systemLight.Should().Equal(explicitLight);
    }

    #endregion

    #region Private Methods

    private static List<string> Failures(string[] foregrounds, string[] backgrounds, double minimum) =>
    [
        .. from theme in themes
           from foreground in foregrounds
           from background in backgrounds
           let text = theme.Value[foreground]
           let surface = theme.Value[background]
           let ratio = Contrast.Ratio(text, surface)
           where ratio < minimum
           select string.Create(
               CultureInfo.InvariantCulture,
               $"{theme.Key}: {foreground} {text} on {background} {surface} is {ratio:F2}:1, below {minimum}:1"),
    ];

    private static void AssertNone(List<string> failures)
    {
        if (failures.Count > 0)
        {
            var listed = string.Join(Environment.NewLine, failures);
            Assert.Fail($"{failures.Count} pairs fall short:{Environment.NewLine}{listed}");
        }
    }

    private static Dictionary<string, string> Declared(Func<StyleRule, bool> where) =>
        rules.Where(where)
            .SelectMany(rule => rule.Declarations)
            .GroupBy(declaration => declaration.Property)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim());

    #endregion
}
