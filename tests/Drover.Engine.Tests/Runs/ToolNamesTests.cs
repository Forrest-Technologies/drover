namespace Drover.Engine.Tests.Runs;

/// <summary>The pure naming rule: two tools of one connection can never be offered to a model under the same name,
/// while a tool that had a name of its own keeps it from run to run.</summary>
public sealed class ToolNamesTests
{
    #region Tests

    [Fact]
    public void A_tool_with_no_clash_keeps_todays_name()
    {
        var tool = Tool("forecast", 0, 1);
        var longTool = Tool(new string('a', 80), 0, 2);

        var name = ToolNames.ForConnection("weather", [tool])[tool.Id];
        var longName = ToolNames.ForConnection("weather", [longTool])[longTool.Id];

        name.Should().Be("weather__forecast");
        longName.Should().Be(ToolNames.ForModel("weather", longTool.Name));
    }

    [Fact]
    public void Two_names_that_clean_to_the_same_text_get_different_names_and_the_first_seen_keeps_the_short_one()
    {
        var first = Tool("repo.get_file", 0, 1);
        var second = Tool("repo_get_file", 1, 2);

        var names = ToolNames.ForConnection("repo", [first, second]);

        names[first.Id].Should().Be("repo__repo_get_file");
        names[second.Id].Should().StartWith("repo__repo_get_file_").And.HaveLength("repo__repo_get_file_".Length + 6);
        names.Values.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Two_long_names_cut_to_the_same_text_get_different_names_within_64_characters()
    {
        var first = Tool($"{new string('a', 38)}one", 0, 1);
        var second = Tool($"{new string('a', 38)}two", 1, 2);

        var names = ToolNames.ForConnection(new string('s', 24), [first, second]);

        names.Values.Should().OnlyHaveUniqueItems().And.OnlyContain(name => name.Length <= 64);
        names[second.Id].Should().HaveLength(64).And.MatchRegex("_([0-9a-f]{6})$");
    }

    [Fact]
    public void The_result_does_not_depend_on_the_order_the_tools_are_given_in()
    {
        var first = Tool("repo.get_file", 0, 1);
        var second = Tool("repo_get_file", 1, 2);
        var third = Tool("repo/get_file", 2, 3);

        var ordered = ToolNames.ForConnection("repo", [first, second, third]);
        var shuffled = ToolNames.ForConnection("repo", [third, first, second]);

        shuffled.Should().BeEquivalentTo(ordered);
    }

    [Fact]
    public void A_missing_tool_still_holds_its_name()
    {
        var granted = Tool("repo.get_file", 1, 1);
        var missing = Tool("repo_get_file", 0, 2);

        var names = ToolNames.ForConnection("repo", [granted, missing]);

        names[missing.Id].Should().Be("repo__repo_get_file", "a tool that went away keeps its claim to the short name");
        names[granted.Id].Should().NotBe("repo__repo_get_file");
    }

    [Fact]
    public void Two_suffixes_that_collide_are_lengthened()
    {
        // Both names clean to x________y, and their SHA-256 values share the first six hex characters.
        var keeper = Tool("x........y", 0, 1);
        var first = Tool("x..//:/ .y", 1, 2);
        var second = Tool("x.//  /..y", 2, 3);

        var names = ToolNames.ForConnection("repo", [keeper, first, second]);

        names.Values.Should().OnlyHaveUniqueItems();
        names[first.Id].Should().EndWith("_51baad06d2");
        names[second.Id].Should().EndWith("_51baad19ec");
    }

    [Fact]
    public void No_name_ever_contains_a_character_a_provider_refuses_or_runs_past_64()
    {
        ToolName[] tools =
        [
            Tool("a.b/c d", 0, 1),
            Tool("a_b-c", 1, 2),
            Tool("éø你好", 2, 3),
            Tool(new string('z', 100), 3, 4),
        ];

        var names = ToolNames.ForConnection("verylongslug", tools);

        names.Values.Should().OnlyContain(name =>
            name.Length <= 64 && name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_' || character == '-'));
    }

    #endregion

    #region Private Methods

    private static ToolName Tool(string name, int order, int index) =>
        new(
            Guid.Parse($"00000000-0000-0000-0000-{index:D12}"),
            Guid.Empty,
            name,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(order));

    #endregion
}
