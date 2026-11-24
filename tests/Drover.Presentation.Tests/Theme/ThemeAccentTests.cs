namespace Drover.Presentation.Tests.Theme;

/// <summary>
/// The black mode and the accents: neither is stored in a theme file, so each is worked out, and what is worked out
/// passes the same gates as what a file holds.
/// </summary>
public sealed class ThemeAccentTests
{
    #region Private Fields

    private static readonly string[] surfaces = ["bg-0", "bg-1", "bg-2", "bg-3"];

    #endregion

    #region Public Methods

    public static TheoryData<string> ThemeIds() =>
        [.. Directory.EnumerateFiles(Repository.ThemeFolder, "*.json").Select(path => Path.GetFileNameWithoutExtension(path)!)];

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Black_is_the_dark_mode_with_a_black_first_surface_and_each_surface_one_level_down(string id)
    {
        var theme = Read(id);
        var (dark, black) = (theme.In(ColorMode.Dark).Colors, theme.In(ColorMode.Black).Colors);

        black["bg-0"].ToString().Should().Be("#000000");
        black["bg-1"].Should().Be(dark["bg-0"]);
        black["bg-2"].Should().Be(dark["bg-1"]);
        black["bg-3"].Should().Be(dark["bg-2"]);
        black.Where(color => !surfaces.Contains(color.Key)).Should().BeSubsetOf(dark);
        black.Should().HaveCount(dark.Count);
    }

    [Fact]
    public void The_gates_run_over_the_black_mode_and_name_it()
    {
        // A visor the color of a head fails in the dark mode, and so in the black mode made from it.
        var night = BuiltInThemes.NightYard;
        Dictionary<string, Rgb> colors = new(night.Dark.Colors) { ["visor"] = night.Dark.Colors["head-0"] };

        ThemeGates.Check(night with { Dark = night.Dark with { Colors = colors } })
            .Should().Contain(failure => failure.Mode == ColorMode.Black && failure.Gate == ThemeGate.BotHues);
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Every_accent_a_theme_offers_leaves_it_passing_every_gate_in_every_mode(string id)
    {
        var theme = Read(id);

        Accents.For(theme).Select(accent => accent.Id).Should().StartWith(Accents.ThemeId);
        foreach (var accent in Accents.For(theme))
        {
            ThemeGates.Check(theme.WithAccent(accent.Id)).Should().BeEmpty($"{id} offers {accent.Id}");
        }
    }

    [Fact]
    public void An_accent_turns_the_hue_of_a_persons_intent_and_nothing_else()
    {
        var theme = BuiltInThemes.Default;
        var blue = theme.WithAccent("blue");

        blue.Dark.Colors["ember"].Hue().Should().BeApproximately(255, 3);
        blue.Light.Colors["ember"].Hue().Should().BeApproximately(255, 3);
        blue.Dark.Colors.Where(color => color.Key is not ("ember" or "on-accent")).Should().BeSubsetOf(theme.Dark.Colors);
        blue.Should().BeEquivalentTo(
            theme,
            options => options.Excluding(file => file.Dark).Excluding(file => file.Light));
        theme.WithAccent("blue").Should().BeSameAs(blue, "a screen tells by the object whether the look changed");
    }

    [Fact]
    public void An_accent_too_close_in_hue_to_a_bot_working_or_to_the_themes_own_color_is_not_offered()
    {
        // Night Yard's bots work in cyan and hand work over in violet, and blue lies between the two.
        Accents.For(BuiltInThemes.NightYard).Select(accent => accent.Id).Should().NotContain("blue");
        BuiltInThemes.NightYard.WithAccent("blue").Should().BeSameAs(BuiltInThemes.NightYard);

        // Front Office's own color is an orange.
        Accents.For(BuiltInThemes.Default).Select(accent => accent.Id).Should().Equal("theme", "green", "blue");
    }

    [Fact]
    public void A_persons_bubble_is_the_card_surface_in_a_document_and_a_tint_of_their_intent_in_bubbles()
    {
        var (night, office) = (BuiltInThemes.NightYard, BuiltInThemes.Default);

        night.Resolve(ColorMode.Dark).Colors["bubble"].Color.Should().Be(night.Dark.Colors["bg-2"]);
        var bubble = office.Resolve(ColorMode.Dark).Colors["bubble"].Color;
        bubble.Should().Be(office.Dark.Colors["ember"].MixWith(office.Dark.Colors["bg-1"], 0.3));
        office.WithAccent("blue").Resolve(ColorMode.Dark).Colors["bubble"].Color.Should().NotBe(bubble);
    }

    [Fact]
    public void The_text_contrast_gate_fails_text_that_cannot_be_read_on_a_persons_bubble()
    {
        var office = BuiltInThemes.Default;
        // An intent as pale as paper tints the dark bubble too light for the secondary text on it.
        Dictionary<string, Rgb> colors = new(office.Dark.Colors) { ["ember"] = Rgb.Parse("#FFFFFF") };

        ThemeGates.Check(office with { Dark = office.Dark with { Colors = colors } }).Should().Contain(failure =>
            failure.Gate == ThemeGate.TextContrast && failure.What.StartsWith("text-2 on the bubble"));
    }

    [Fact]
    public void A_color_made_from_a_lightness_a_chroma_and_a_hue_reads_back_as_them_and_keeps_its_hue_when_brought_into_range()
    {
        var (lightness, chroma, hue) = Rgb.FromOklch(0.7, 0.1, 150).ToOklch();

        lightness.Should().BeApproximately(0.7, 0.01);
        chroma.Should().BeApproximately(0.1, 0.01);
        hue.Should().BeApproximately(150, 2);
        Rgb.FromOklch(0.7, 0.4, 255).Hue().Should().BeApproximately(255, 3);
    }

    [Fact]
    public void A_rounded_theme_takes_large_radii_and_no_cuts_and_a_square_one_cannot()
    {
        var text = File.ReadAllText(Path.Combine(Repository.ThemeFolder, "front-office.json"));

        ThemeFileReader.Read(text, "front-office").Shape.Should().BeEquivalentTo(
            new ThemeShape(CornerLanguage.Rounded, new(0, 0), false, 20, 18, 24, 1, 1.75));
        ThemeGates.CheckFile(text.Replace("\"rounded\"", "\"square\""), "front-office")
            .Should().Contain(failure => failure.Gate == ThemeGate.Bounds && failure.What.Contains("shape.radius.control"));
        ThemeGates.CheckFile(text.Replace("\"bubbles\"", "\"cards\""), "front-office")
            .Should().Contain(failure => failure.Gate == ThemeGate.Bounds && failure.What.Contains("surfaces.conversation"));
    }

    #endregion

    #region Private Methods

    private static ThemeFile Read(string id) =>
        ThemeFileReader.Read(File.ReadAllText(Path.Combine(Repository.ThemeFolder, $"{id}.json")), id);

    #endregion
}
