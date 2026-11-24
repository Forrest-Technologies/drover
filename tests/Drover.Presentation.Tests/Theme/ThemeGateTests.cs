namespace Drover.Presentation.Tests.Theme;

/// <summary>
/// The accessibility gates, run over every theme file in the folder and over deliberately broken themes, one for
/// each gate, so a gate that stopped checking would be found here. A theme that breaks a gate cannot merge.
/// </summary>
public sealed class ThemeGateTests
{
    #region Public Methods

    /// <summary>
    /// Every file in the theme folder. A new theme is a new file there, and passes these gates or does not merge.
    /// </summary>
    public static TheoryData<string> ThemeFiles() =>
    [
        .. Directory.EnumerateFiles(Repository.ThemeFolder, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)).Order(),
    ];

    [Theory]
    [MemberData(nameof(ThemeFiles))]
    public void Every_theme_file_passes_every_gate_in_both_modes(string id)
    {
        var json = File.ReadAllText(Path.Combine(Repository.ThemeFolder, $"{id}.json"));

        ThemeGates.CheckFile(json, id).Select(failure => failure.ToString()).Should().BeEmpty();
    }

    [Fact]
    public void The_theme_folder_is_found_and_holds_night_yard_and_more_than_one_theme()
    {
        var files = Directory.EnumerateFiles(Repository.ThemeFolder, "*.json").ToList();

        files.Should().Contain(path => path.EndsWith("night-yard.json", StringComparison.Ordinal));
        files.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void Text_contrast_fails_below_4_5_to_1_and_names_the_theme_mode_pair_and_ratio()
    {
        var failures = ThemeGates.Check(Night(dark: colors => colors["text-2"] = Rgb.Parse("#5A6272")));

        failures.Should().Contain(failure => failure.Gate == ThemeGate.TextContrast);
        failures.First(failure => failure.Gate == ThemeGate.TextContrast).ToString()
            .Should().StartWith("night-yard, dark: TextContrast: text-2 on bg-").And.Contain(":1, needs 4.5:1");
    }

    [Fact]
    public void Text_contrast_fails_a_status_color_in_the_light_mode_and_text_on_the_accent()
    {
        Fails(Night(light: colors => colors["danger"] = Rgb.Parse("#E88080")), ThemeGate.TextContrast, "danger on");
        var pale = Night(dark: colors => colors["on-accent"] = Rgb.Parse("#FFB454"));

        Fails(pale, ThemeGate.TextContrast, "on-accent on");
    }

    [Fact]
    public void Interface_contrast_fails_a_control_edge_or_a_focus_ring_below_3_to_1()
    {
        // The decorative line is exempt. text-3 draws a control's edge and ember its focus ring.
        Fails(Night(dark: colors => colors["text-3"] = Rgb.Parse("#2E3A4E")), ThemeGate.InterfaceContrast, "text-3 on");
        Fails(Night(light: colors => colors["ember"] = Rgb.Parse("#F2E3C8")), ThemeGate.InterfaceContrast, "ember on");
    }

    [Fact]
    public void Bot_hues_fail_a_hue_below_4_5_to_1_and_a_head_below_3_to_1_against_the_visor()
    {
        Fails(Night(dark: colors => colors["hue-4"] = Rgb.Parse("#3D4A20")), ThemeGate.BotHues, "hue-4 on");

        foreach (var visor in (string[])["visor", "visor-lit", "visor-failed"])
        {
            Fails(Night(dark: colors => colors["head-7"] = colors[visor]), ThemeGate.BotHues, $"{visor} on head-7");
        }
    }

    [Fact]
    public void Bounds_fail_a_value_outside_the_format_and_name_it()
    {
        foreach (var (group, key, value) in new (string, string, JsonNode)[]
        {
            ("density", "spacing", 1.6),
            ("motion", "scale", 2),
            ("motion", "bloom", 12),
            ("shape", "line", 3),
        })
        {
            var failures = ThemeGates.CheckFile(EditedJson(night => night[group]![key] = value), "night-yard");

            failures.Should().ContainSingle(failure => failure.Gate == ThemeGate.Bounds)
                .Which.What.Should().Contain($"{group}.{key}");
        }

        var scale = EditedJson(night => night["type"]!["scale"] = new JsonArray(12, 13, 17, 18, 20, 28));
        var radius = EditedJson(night => night["shape"]!["radius"]!["card"] = 40);

        ThemeGates.CheckFile(scale, "night-yard").Should().ContainSingle()
            .Which.What.Should().Contain("type.scale");
        ThemeGates.CheckFile(radius, "night-yard").Should().ContainSingle()
            .Which.What.Should().Contain("shape.radius.card");
    }

    [Fact]
    public void Touch_targets_fail_a_control_or_a_row_shorter_than_44_for_a_finger_or_32_for_a_pointer()
    {
        var night = BuiltInThemes.NightYard;
        var low = night with { Density = night.Density with { Control = new(28, 40), Row = new(30, 43) } };

        ThemeGates.Check(low).Where(failure => failure.Gate == ThemeGate.TouchTargets)
            .Select(failure => failure.What).Should().Equal(
                "control height for a pointer is 28 px, needs 32 px",
                "control height for a finger is 40 px, needs 44 px",
                "list row height for a pointer is 30 px, needs 32 px",
                "list row height for a finger is 43 px, needs 44 px");
    }

    [Fact]
    public void Invariants_fail_two_accents_too_close_in_hue_and_a_gradient_or_shadow_the_file_names()
    {
        // Violet moved to nearly ion's hue.
        var close = Night(dark: colors => colors["violet"] = Rgb.Parse("#6FE0F2"));

        Fails(close, ThemeGate.Invariants, "ion and violet are");

        foreach (var word in (string[])["gradient", "boxShadow", "Shadow"])
        {
            var failures = ThemeGates.CheckFile(EditedJson(night => night["surfaces"]![word] = "none"), "night-yard");

            failures.Should().Contain(failure => failure.Gate == ThemeGate.Invariants && failure.What.Contains(word));
        }
    }

    [Fact]
    public void A_file_that_is_not_json_fails_the_bounds_gate_once_and_nothing_throws() =>
        ThemeGates.CheckFile("{ not json", "night-yard").Should().ContainSingle()
            .Which.Gate.Should().Be(ThemeGate.Bounds);

    [Fact]
    public void Night_yard_is_clean_and_a_theme_broken_for_each_gate_fails_that_gate()
    {
        var night = BuiltInThemes.NightYard;
        ThemeGates.Check(night).Should().BeEmpty();

        var shortControls = night with { Density = night.Density with { Control = new(20, 20) } };
        var lines = EditedJson(file => file["density"]!["composerLines"] = 40);
        var gradient = EditedJson(file => file["motion"]!["gradient"] = 1);
        var faint = Rgb.Parse("#C9CFDA");

        var broken = new Dictionary<ThemeGate, IReadOnlyList<GateFailure>>
        {
            [ThemeGate.TextContrast] = ThemeGates.Check(Night(dark: colors => colors["warn"] = Rgb.Parse("#6B5A10"))),
            [ThemeGate.InterfaceContrast] = ThemeGates.Check(Night(light: colors => colors["text-3"] = faint)),
            [ThemeGate.BotHues] = ThemeGates.Check(Night(light: colors => colors["head-3"] = colors["visor-lit"])),
            [ThemeGate.Bounds] = ThemeGates.CheckFile(lines, "night-yard"),
            [ThemeGate.TouchTargets] = ThemeGates.Check(shortControls),
            [ThemeGate.Invariants] = ThemeGates.CheckFile(gradient, "night-yard"),
        };

        foreach (var gate in Enum.GetValues<ThemeGate>())
        {
            broken[gate].Should().Contain(failure => failure.Gate == gate, $"a theme broken for {gate} must fail it");
        }
    }

    #endregion

    #region Private Methods

    private static void Fails(ThemeFile theme, ThemeGate gate, string pair) =>
        ThemeGates.Check(theme).Should().Contain(
            failure => failure.Gate == gate && failure.What.StartsWith(pair, StringComparison.Ordinal),
            $"{pair} breaks {gate}");

    // Night Yard with some colors changed, in one mode or the other.
    private static ThemeFile Night(
        Action<Dictionary<string, Rgb>>? dark = null,
        Action<Dictionary<string, Rgb>>? light = null)
    {
        var night = BuiltInThemes.NightYard;

        return night with { Dark = Changed(night.Dark, dark), Light = Changed(night.Light, light) };
    }

    private static ModeColors Changed(ModeColors mode, Action<Dictionary<string, Rgb>>? change)
    {
        var colors = mode.Colors.ToDictionary(pair => pair.Key, pair => pair.Value);
        change?.Invoke(colors);

        return mode with { Colors = colors };
    }

    private static string EditedJson(Action<JsonNode> change)
    {
        var night = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.ThemeFolder, "night-yard.json")))!;
        change(night);

        return night.ToJsonString();
    }

    #endregion
}
