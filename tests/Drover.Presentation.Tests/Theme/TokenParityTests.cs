namespace Drover.Presentation.Tests.Theme;

/// <summary>
/// The native client's token file carries every token of the web client's, under its predictable name, with the
/// same value. The web file is the specification; this is what keeps the two from drifting.
/// </summary>
public sealed partial class TokenParityTests
{
    #region Private Fields

    private const double Rem = 16;

    // Tokens whose web value is not one number or one color. Each is carried in the native file in the form its
    // platform takes, and is checked by its own test below.
    private static readonly Dictionary<string, string> restated = new()
    {
        ["scrim"] = "the first surface at 72 percent",
        ["font-ui"] = "ms-appx:///Assets/Fonts/Inter.ttf#Inter",
        ["font-mono"] = "ms-appx:///Assets/Fonts/JetBrainsMono.ttf#JetBrains Mono",
        ["weight-body"] = "Normal",
        ["weight-label"] = "Medium",
        ["weight-heading"] = "SemiBold",
        ["measure"] = "572",
        ["tracking"] = "10",
        ["image-ratio"] = "1.7777777777777777",
        ["composer-lines"] = "8",
        ["output-lines"] = "12",
        ["travel-width"] = "0.25",
        ["ease"] = "0.2,0.8 0.2,1",
        ["bloom-ion"] = "8",
        ["bloom-violet"] = "8",
    };

    #endregion

    #region Public Methods

    [Fact]
    public void Every_color_of_the_web_client_is_in_the_native_file_in_both_themes_with_the_same_value()
    {
        var dark = NativeTokens.Colors("Default");
        var light = NativeTokens.Colors("Light");
        var problems = new List<string>();

        foreach (var (token, value) in NativeTokens.WebDark().Where(pair => pair.Value.StartsWith('#')))
        {
            Compare(problems, "dark", token, value, dark);
            Compare(problems, "light", token, NativeTokens.WebLight()[token], light);
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void A_subtree_shown_dark_gets_the_same_colors_as_the_app_does_by_default() =>
        NativeTokens.Colors("Dark").Should().BeEquivalentTo(NativeTokens.Colors("Default"));

    [Fact]
    public void Every_color_has_a_brush_of_the_same_name_in_every_theme()
    {
        foreach (var theme in (string[])["Default", "Dark", "Light"])
        {
            var colors = NativeTokens.Colors(theme);
            var brushes = NativeTokens.Brushes(theme);

            brushes.Keys.Should().BeEquivalentTo(colors.Keys.Select(key => $"{key}Brush"));
            brushes.Should().AllSatisfy(brush => brush.Value.Should().Be($"{{StaticResource {brush.Key[..^"Brush".Length]}}}"));
        }
    }

    [Fact]
    public void Every_length_size_and_duration_of_the_web_client_is_in_the_native_file_with_the_same_value()
    {
        var native = NativeTokens.Values();
        var problems = new List<string>();

        foreach (var (token, value) in NativeTokens.WebDark().Where(pair => !pair.Value.StartsWith('#') && !restated.ContainsKey(pair.Key)))
        {
            var number = Number().Match(value);
            if (!number.Success)
            {
                problems.Add($"--dv-{token} is '{value}', which is neither one number nor on the list of restated tokens");
                continue;
            }

            var expected = double.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture) * (number.Groups[2].Value == "rem" ? Rem : 1);
            if (!native.TryGetValue(NativeTokens.NameOf(token), out var found))
            {
                problems.Add($"--dv-{token} has no {NativeTokens.NameOf(token)} in Tokens.xaml");
            }
            else if (double.Parse(found, CultureInfo.InvariantCulture) != expected)
            {
                problems.Add($"{NativeTokens.NameOf(token)} is {found}, and --dv-{token} is {value}");
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void A_token_that_is_not_one_number_is_restated_in_the_form_the_platform_takes()
    {
        var native = NativeTokens.Values();
        var web = NativeTokens.WebDark();

        foreach (var (token, expected) in restated.Where(pair => pair.Key != "scrim"))
        {
            web.Should().ContainKey(token);
            native.Should().ContainKey(NativeTokens.NameOf(token)).WhoseValue.Should().Be(expected);
        }
    }

    [Fact]
    public void The_scrim_is_the_first_surface_at_72_percent_in_both_themes()
    {
        NativeTokens.WebDark()["scrim"].Should().Be("color-mix(in srgb, var(--dv-bg-0) 72%, transparent)");

        foreach (var theme in (string[])["Default", "Light"])
        {
            var colors = NativeTokens.Colors(theme);
            colors["DvScrim"].Should().Be($"#B8{colors["DvBg0"][1..]}");
        }
    }

    [Fact]
    public void The_yard_dot_is_the_line_color_at_70_percent_in_both_themes()
    {
        foreach (var theme in (string[])["Default", "Light"])
        {
            var colors = NativeTokens.Colors(theme);
            colors["DvYardDot"].Should().Be($"#B3{colors["DvLine"][1..]}");
        }
    }

    [Fact]
    public void The_native_file_holds_no_token_the_web_client_does_not_have()
    {
        var web = NativeTokens.WebDark().Keys.Select(NativeTokens.NameOf).ToHashSet();
        var extra = NativeTokens.Values().Keys.Concat(NativeTokens.Colors("Default").Keys)
            .Where(key => !web.Contains(key) && key != "DvYardDot");

        extra.Should().BeEmpty("a value both clients need is added to tokens.css first");
    }

    #endregion

    #region Private Methods

    private static void Compare(List<string> problems, string theme, string token, string expected, Dictionary<string, string> native)
    {
        var name = NativeTokens.NameOf(token);
        if (!native.TryGetValue(name, out var found))
        {
            problems.Add($"--dv-{token} has no {name} in the {theme} theme of Tokens.xaml");
        }
        else if (!string.Equals(found, expected, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{name} is {found} in the {theme} theme, and --dv-{token} is {expected}");
        }
    }

    [GeneratedRegex(@"^([0-9.]+)(px|rem|ms)?$")]
    private static partial Regex Number();

    #endregion
}
