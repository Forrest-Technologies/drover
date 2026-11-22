namespace Drover.Presentation.Tests.Theme;

/// <summary>What a theme file resolves to: Night Yard stays exactly what the token file holds today.</summary>
public sealed class ThemeResolutionTests
{
    #region Private Fields

    private static readonly string[] lineTokens = ["line-0", "line-1", "line-2", "line-h1", "line-h2", "line-h3"];

    private static readonly string[] namedTokens = ["line-0", "line-2", "line-h1"];

    private static readonly Dictionary<int, string> weightNames = new() { [400] = "Normal", [500] = "Medium", [600] = "SemiBold", [700] = "Bold" };

    #endregion

    #region Public Methods

    [Theory]
    [InlineData(ColorMode.Dark, "Default")]
    [InlineData(ColorMode.Dark, "Dark")]
    [InlineData(ColorMode.Light, "Light")]
    public void Night_yard_resolves_to_every_color_of_the_token_file_in_each_mode(ColorMode mode, string dictionary)
    {
        var resolved = BuiltInThemes.NightYard.Resolve(mode).Colors;
        var problems = new List<string>();

        foreach (var (name, hex) in NativeTokens.Colors(dictionary))
        {
            var token = resolved.Where(color => NativeTokens.NameOf(color.Key) == name).ToList();
            if (token.Count != 1 || !string.Equals(token[0].Value.ToString(), hex, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{name} is {hex} in the token file and {(token.Count == 1 ? token[0].Value.ToString() : "missing")} from Night Yard's file");
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void Night_yard_resolves_to_every_number_of_the_token_file_it_sets()
    {
        var resolved = BuiltInThemes.NightYard.Resolve(ColorMode.Dark).Numbers;
        var native = NativeTokens.Values();
        var problems = new List<string>();

        foreach (var (token, value) in resolved)
        {
            var name = TokenNames.Of(token);
            if (!native.TryGetValue(name, out var found))
            {
                continue;
            }

            if (token.StartsWith("weight-", StringComparison.Ordinal) ? found != weightNames[(int)value] : double.Parse(found, CultureInfo.InvariantCulture) != value)
            {
                problems.Add($"{name} is {found} in the token file and {value} from Night Yard's file");
            }
        }

        problems.Should().BeEmpty();
        resolved.Keys.Where(token => native.ContainsKey(TokenNames.Of(token))).Should().HaveCountGreaterThan(30, "most numbers are compared");
    }

    [Fact]
    public void Night_yard_resolves_the_line_heights_and_leads_the_style_file_held_as_constants()
    {
        var numbers = BuiltInThemes.NightYard.Resolve(ColorMode.Dark).Numbers;

        lineTokens.Select(token => numbers[token]).Should().Equal(18, 19.5, 21, 35, 25, 20);
        (numbers["lead-body"], numbers["lead-mono"]).Should().Be((2, 1));
        namedTokens.Select(TokenNames.Of).Should().Equal("DvLine12", "DvLine14", "DvLineH1");
    }

    [Fact]
    public void Night_yard_draws_in_the_two_faces_the_token_file_names()
    {
        var faces = BuiltInThemes.NightYard.Resolve(ColorMode.Dark).Faces;
        var native = NativeTokens.Values();

        faces.Interface.Asset.Should().Be(native["DvFontUi"]);
        faces.Machine.Asset.Should().Be(native["DvFontMono"]);
        faces.Reading.Should().Be(faces.Interface);
        faces.Heading.Should().Be(faces.Interface);
        faces.Numerals.Should().Be(faces.Interface);
    }

    [Fact]
    public void The_scrim_is_the_first_surface_at_72_percent_and_the_yard_color_is_the_line_at_the_yard_strength()
    {
        var dark = BuiltInThemes.NightYard.Resolve(ColorMode.Dark).Colors;

        dark["scrim"].ToString().Should().Be("#B805070A");
        dark["yard-dot"].ToString().Should().Be("#B3263042");
        dark["list"].ToString().Should().Be("#05070A", "two surface levels rest lists on the first surface");
    }

    [Fact]
    public void Spacing_steps_come_from_the_multiplier_each_rounded_to_a_whole_pixel_and_at_least_2()
    {
        var tight = Edited(file => file with { Density = file.Density with { Spacing = 0.85 } }).Numbers;
        var loose = Edited(file => file with { Density = file.Density with { Spacing = 1.25 } }).Numbers;

        Enumerable.Range(1, 7).Select(step => tight[$"space-{step}"]).Should().Equal(3, 7, 10, 14, 20, 27, 41);
        Enumerable.Range(1, 7).Select(step => loose[$"space-{step}"]).Should().Equal(5, 10, 15, 20, 30, 40, 60);
    }

    [Fact]
    public void Durations_come_from_the_scale_and_bloom_and_the_cycles_are_taken_as_they_are()
    {
        var slow = Edited(file => file with { Motion = file.Motion with { Scale = 1.3, Bloom = 3, Breath = 3380, Travel = 1690 } }).Numbers;

        Enumerable.Range(1, 3).Select(step => slow[$"dur-{step}"]).Should().Equal(156, 286, 546);
        (slow["bloom-ion"], slow["bloom-violet"], slow["dur-breath"], slow["dur-travel"]).Should().Be((3, 3, 3380, 1690));
    }

    [Fact]
    public void The_type_scale_the_weights_and_the_measure_come_from_the_file()
    {
        var small = Edited(file => file with { Type = file.Type with { Scale = [11, 12, 13, 15, 18, 24], MachineNumerals = true, Headings = HeadingFace.Machine } });

        Enumerable.Range(0, 6).Select(step => small.Numbers[$"size-{step}"]).Should().Equal(11, 12, 13, 15, 18, 24);
        small.Numbers["measure"].Should().Be(Math.Round(65 * (22 / 35.0) * 13));
        small.Faces.Heading.Id.Should().Be("jetbrains-mono");
        small.Faces.Numerals.Id.Should().Be("jetbrains-mono");
    }

    [Fact]
    public void A_face_that_is_not_bundled_yet_is_drawn_in_the_bundled_face_of_its_kind()
    {
        var reading = Edited(file => file with { Type = file.Type with { Reading = "literata" } }).Faces;

        reading.Reading.Id.Should().Be("inter");
        FontFaces.Drawn("atkinson-hyperlegible-next").Id.Should().Be("inter");
    }

    [Fact]
    public void Three_surface_levels_rest_lists_on_the_second_surface()
    {
        var tiered = Edited(file => file with { Surfaces = file.Surfaces with { Levels = 3 } });

        tiered.Colors["list"].Should().Be(tiered.Colors["bg-1"]);
    }

    #endregion

    #region Private Methods

    private static ThemeTokenSet Edited(Func<ThemeFile, ThemeFile> change) => change(BuiltInThemes.NightYard).Resolve(ColorMode.Dark);

    #endregion
}
