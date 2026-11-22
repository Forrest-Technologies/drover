namespace Drover.Presentation.Tests.Theme;

/// <summary>
/// The reader of the closed theme-file format. These checks are the small local ones: the shared checker of issue
/// 778 replaces them, and the accessibility gates of issue 423 add the contrast rules.
/// </summary>
public sealed class ThemeFileTests
{
    #region Private Fields

    private static readonly string[] shipped = ["night-yard", "signal-room", "paper-ledger", "workbench", "blueprint", "quiet-hours", "foundry"];

    #endregion

    #region Public Methods

    public static TheoryData<string> ThemeIds() => [.. shipped];

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Every_theme_file_of_the_design_is_in_the_format(string id)
    {
        var file = ThemeFileReader.Read(File.ReadAllText(Path.Combine(Repository.ThemeFolder, $"{id}.json")), id);

        file.Id.Should().Be(id);
        file.Dark.Colors.Should().HaveCount(43);
        file.Light.Colors.Should().HaveCount(43);
    }

    [Fact]
    public void The_theme_folder_holds_the_seven_themes_and_nothing_else()
    {
        Directory.EnumerateFiles(Repository.ThemeFolder).Select(Path.GetFileNameWithoutExtension)
            .Should().BeEquivalentTo(shipped);
    }

    [Fact]
    public void Night_yard_is_the_one_theme_that_ships_through_the_pipeline_and_is_read_from_its_file()
    {
        BuiltInThemes.All.Select(theme => theme.Id).Should().Equal("night-yard");
        BuiltInThemes.NightYard.Should().BeEquivalentTo(ThemeFileReader.Read(File.ReadAllText(Path.Combine(Repository.ThemeFolder, "night-yard.json")), "night-yard"));
        BuiltInThemes.Get("signal-room").Should().BeNull("a theme joins the app when the issue that ships it embeds its file");
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void Every_theme_of_the_first_batch_is_in_the_expressive_group(string id) =>
        ThemeFileReader.Read(File.ReadAllText(Path.Combine(Repository.ThemeFolder, $"{id}.json")), id)
            .Group.Should().Be(ThemeGroup.Expressive);

    [Fact]
    public void A_group_is_expressive_or_professional_and_nothing_else()
    {
        ProblemsOf(Edit(night => night["group"] = "playful")).Should().ContainSingle().Which.Should().Contain("group");
        ProblemsOf(Edit(night => night.AsObject().Remove("group"))).Should().ContainSingle().Which.Should().Contain("group");
    }

    [Fact]
    public void A_file_with_a_key_the_format_does_not_have_is_refused()
    {
        var problems = ProblemsOf(Edit(night => night["motion"]!["glow"] = 3));

        problems.Should().ContainSingle().Which.Should().Contain("motion").And.Contain("glow");
    }

    [Fact]
    public void A_file_missing_a_key_is_refused()
    {
        var problems = ProblemsOf(Edit(night => night["shape"]!.AsObject().Remove("line")));

        problems.Should().ContainSingle().Which.Should().Contain("shape").And.Contain("line");
    }

    [Fact]
    public void A_color_is_only_ever_six_hex_digits()
    {
        foreach (var bad in (string[])["#FFF", "#FFB454FF", "red", "rgb(1,2,3)", "FFB454"])
        {
            var problems = ProblemsOf(Edit(night => night["dark"]!["color"]!["ember"] = bad));

            problems.Should().ContainSingle().Which.Should().Contain("dark.color.ember", $"'{bad}' is not #RRGGBB");
        }
    }

    [Fact]
    public void A_value_outside_its_bounds_is_refused()
    {
        ProblemsOf(Edit(night => night["density"]!["spacing"] = 1.4)).Should().ContainSingle().Which.Should().Contain("density.spacing");
        ProblemsOf(Edit(night => night["type"]!["scale"] = new JsonArray(12, 13, 17, 16, 20, 28))).Should().ContainSingle().Which.Should().Contain("type.scale");
        ProblemsOf(Edit(night => night["motion"]!["bloom"] = 9)).Should().ContainSingle().Which.Should().Contain("motion.bloom");
    }

    [Fact]
    public void A_whole_number_must_be_written_whole()
    {
        ProblemsOf(Edit(night => night["shape"]!["line"] = 1.5)).Should().ContainSingle().Which.Should().Contain("shape.line");
    }

    [Fact]
    public void A_face_the_app_does_not_bundle_and_a_font_path_are_both_refused()
    {
        ProblemsOf(Edit(night => night["type"]!["interface"] = "comic-sans")).Should().ContainSingle().Which.Should().Contain("type.interface");
        ProblemsOf(Edit(night => night["type"]!["interface"] = "https://example.test/face.ttf")).Should().NotBeEmpty();
        ProblemsOf(Edit(night => night["type"]!["machine"] = "inter")).Should().ContainSingle().Which.Should().Contain("type.machine");
    }

    [Fact]
    public void Rules_between_two_keys_hold()
    {
        ProblemsOf(Edit(night => night["shape"]!["registration"] = true)).Should().ContainSingle().Which.Should().Contain("registration");
        ProblemsOf(Edit(night => night["motion"]!["bloom"] = 0)).Should().ContainSingle().Which.Should().Contain("outlined");
        ProblemsOf(Edit(night => night["surfaces"]!["yard"]!["texture"] = "none")).Should().ContainSingle().Which.Should().Contain("strength");
    }

    [Fact]
    public void The_id_must_be_the_files_name()
    {
        ProblemsOf(Edit(_ => { }), expectedId: "signal-room").Should().ContainSingle().Which.Should().Contain("signal-room");
    }

    [Fact]
    public void Text_that_is_not_json_is_refused_with_one_problem_and_a_duplicate_key_is_refused()
    {
        ProblemsOf("{ not json").Should().ContainSingle().Which.Should().Contain("not JSON");
        ProblemsOf("""{"format":1,"format":1}""").Should().Contain(problem => problem.Contains("twice"));
    }

    [Fact]
    public void Every_problem_in_a_file_is_listed_in_one_pass()
    {
        var problems = ProblemsOf(Edit(night =>
        {
            night["density"]!["spacing"] = 5;
            night["motion"]!["scale"] = 5;
            night["dark"]!["color"]!["bg-0"] = "nope";
        }));

        problems.Should().HaveCount(3);
    }

    #endregion

    #region Private Methods

    private static string Edit(Action<JsonNode> change)
    {
        var night = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.ThemeFolder, "night-yard.json")))!;
        change(night);

        return night.ToJsonString();
    }

    private static IReadOnlyList<string> ProblemsOf(string json, string expectedId = "night-yard") =>
        Assert.Throws<ThemeFormatException>(() => ThemeFileReader.Read(json, expectedId)).Problems;

    #endregion
}
