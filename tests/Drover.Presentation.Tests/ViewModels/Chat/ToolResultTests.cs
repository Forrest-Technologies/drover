namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// A tool's result as a person reads it: stored as JSON for the model, shown decoded, so tabs and line breaks are
/// tabs and line breaks and never <c>\t</c> and <c>\n</c>.
/// </summary>
public sealed class ToolResultTests
{
    #region Private Fields

    private static readonly Guid callId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1");

    private const string Rows = "chain\tname\tprice\nsolana\tCYBERLEEK\t0.002139\n";

    #endregion

    #region Commands

    [Fact]
    public void A_stored_command_shows_its_output_with_real_tabs_and_line_breaks_and_no_json()
    {
        var stored = JsonSerializer.Serialize(new { exit_code = 0, stdout = Rows, stderr = "", truncated = false, error = (string?)null });

        var card = Stored("sandbox_run", """{"command":"cat rows.tsv"}""", stored);

        card.Output.Should().Equal([new ToolOutputPiece("stdout", Rows)]);
        card.Result.Should().BeNull("the output and the exit code say everything the result holds");
        card.ExitCode.Should().Be(0);
    }

    [Fact]
    public void A_command_result_cut_off_inside_its_output_still_shows_what_it_holds_decoded()
    {
        var whole = JsonSerializer.Serialize(new { exit_code = 0, stdout = Rows });
        var cut = whole[..(whole.IndexOf("CYBER", StringComparison.Ordinal) + 3)];
        var call = Call("sandbox.run", """{"command":"cat rows.tsv"}""") with { Preview = cut };

        var card = call.ToCard();

        card.Output.Should().Equal([new ToolOutputPiece("stdout", "chain\tname\tprice\nsolana\tCYB")]);
        card.Result.Should().BeNull("a cut off result is never shown as JSON text");
        card.ExitCode.Should().Be(0);
    }

    [Fact]
    public void Live_output_is_kept_and_the_result_says_only_what_the_output_does_not()
    {
        var stored = JsonSerializer.Serialize(new { exit_code = 0, stdout = "ok", truncated = true, note = "This is a new sandbox." });
        var call = Call("sandbox.run", """{"command":"make"}""") with { Preview = stored, Pieces = [new("stdout", "ok")] };

        var card = call.ToCard();

        card.Output.Should().Equal([new ToolOutputPiece("stdout", "ok")]);
        card.Result.Should().Be($"{ToolResults.OutputCutOff}\nThis is a new sandbox.");
    }

    #endregion

    #region Other Tools

    [Fact]
    public void A_tools_text_result_shows_as_its_text_without_quotes_or_escapes()
    {
        var card = Stored("search_web", """{"query":"tides"}""", JsonSerializer.Serialize("first line\n\tindented \"quoted\" é"));

        card.Result.Should().Be("first line\n\tindented \"quoted\" é");
        card.ResultLanguage.Should().BeNull();
    }

    [Fact]
    public void A_text_result_cut_off_inside_an_escape_shows_what_came_before()
    {
        var call = Call("search_web", """{"query":"tides"}""") with { Preview = "\"first\\nsecond\\u00" };

        call.ToCard().Result.Should().Be("first\nsecond");
    }

    [Fact]
    public void A_file_read_shows_the_files_text_in_the_language_its_name_says()
    {
        var stored = JsonSerializer.Serialize(new { path = "/home/user/Tool.CS", content = "class Tool\n{\n}\n", size = 14 });

        var card = Stored("sandbox_read_file", """{"path":"/home/user/Tool.CS"}""", stored);

        card.Result.Should().Be("class Tool\n{\n}\n");
        card.ResultLanguage.Should().Be("cs");
    }

    [Fact]
    public void A_structured_result_is_shown_as_indented_json_and_labeled_json()
    {
        var card = Stored("weather", "{}", """{"city":"Oslo","hours":[1,2]}""");

        card.Result.Should().Contain("\"city\": \"Oslo\"").And.Contain("\n");
        card.ResultLanguage.Should().Be(ToolResults.JsonLanguage);
        card.ResultLength.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Plain_text_that_is_not_json_is_shown_as_it_came()
    {
        var call = Call("weather", "{}") with { Preview = "sunny\tdry" };

        call.ToCard().Result.Should().Be("sunny\tdry");
    }

    #endregion

    #region Results That Cannot Be Read

    [Theory]
    [InlineData("sandbox.run", "{\"exit_code\":0,\"stdout\":oops}")]
    [InlineData("sandbox.run", "{\"exit_code\":0,\"stdout\":\"a\" \"b\"")]
    [InlineData("weather", "[\"a\" \"b\"]")]
    [InlineData("weather", "{\"city\" \"Oslo\"}")]
    public void A_malformed_result_is_shown_as_it_came_and_never_fails_the_card(string tool, string malformed)
    {
        var call = Call(tool, "{}") with { Preview = malformed };

        var card = call.Invoking(called => called.ToCard()).Should().NotThrow().Subject;

        card.Result.Should().Be(malformed);
        card.ResultLanguage.Should().BeNull();
        card.Output.Should().BeEmpty("a result that is not JSON holds no output to decode");
    }

    [Fact]
    public void A_malformed_stored_result_is_shown_as_it_was_stored()
    {
        var malformed = "{\"city\":\"Oslo\",}";

        var card = FluentActions.Invoking(() => Stored("weather", "{}", malformed)).Should().NotThrow().Subject;

        card.Result.Should().Be(malformed);
    }

    [Theory]
    [InlineData("sandbox.run")]
    [InlineData("weather")]
    public void A_result_nested_deeper_than_the_read_budget_is_shown_as_it_came(string tool)
    {
        var deep = $"{new string('[', ToolResults.MaxReadDepth + 1)}{new string(']', ToolResults.MaxReadDepth + 1)}";
        var call = Call(tool, "{}") with { Preview = deep };

        var card = call.Invoking(called => called.ToCard()).Should().NotThrow().Subject;

        card.Result.Should().Be(deep);
        card.ResultLanguage.Should().BeNull();
    }

    [Fact]
    public void A_stored_result_nested_deeper_than_the_read_budget_is_shown_as_it_was_stored()
    {
        var deep = $"{string.Concat(Enumerable.Repeat("{\"a\":", ToolResults.MaxReadDepth + 1))}1{new string('}', ToolResults.MaxReadDepth + 1)}";

        var card = FluentActions.Invoking(() => Stored("weather", "{}", deep)).Should().NotThrow().Subject;

        card.Result.Should().Be(deep);
    }

    [Fact]
    public void A_cut_off_result_is_read_as_far_as_it_goes_where_an_invalid_one_is_shown_as_it_came()
    {
        var cut = Call("sandbox.run", "{}") with { Preview = "{\"exit_code\":0,\"stdout\":\"a\\tb" };
        var invalid = Call("sandbox.run", "{}") with { Preview = "{\"exit_code\":0,\"stdout\":\"a\\tb\" x" };

        var cutCard = cut.ToCard();
        var invalidCard = invalid.ToCard();

        cutCard.Output.Should().Equal([new ToolOutputPiece("stdout", "a\tb")]);
        cutCard.Result.Should().BeNull("the start of a result is told by what it holds so far");
        invalidCard.Output.Should().BeEmpty();
        invalidCard.Result.Should().Be(invalid.Preview);
    }

    [Theory]
    [InlineData("sandbox.run")]
    [InlineData("weather")]
    public void A_result_longer_than_the_read_budget_is_shown_as_it_came_without_being_read(string tool)
    {
        var text = new string('x', ToolResults.MaxReadLength);
        var stored = JsonSerializer.Serialize(new { exit_code = 0, stdout = text });
        var call = Call(tool, "{}") with { Preview = stored };

        var card = call.Invoking(called => called.ToCard()).Should().NotThrow().Subject;

        card.Result.Should().Be(stored);
        card.ResultLanguage.Should().BeNull();
        card.Output.Should().BeEmpty("a result too long to read for display is not decoded");
        card.ResultLength.Should().Be(stored.Length);
    }

    [Fact]
    public void A_stored_command_result_that_indentation_would_push_past_the_budget_still_shows_its_output()
    {
        var padding = Enumerable.Repeat(0, (ToolResults.MaxReadLength / 2) - 64).ToArray();
        var stored = JsonSerializer.Serialize(new { exit_code = 0, stdout = Rows, padding });
        stored.Length.Should().BeLessThan(ToolResults.MaxReadLength, "the compact result is inside the budget");

        var card = Stored("sandbox_run", """{"command":"cat rows.tsv"}""", stored);

        card.Output.Should().Equal([new ToolOutputPiece("stdout", Rows)], "the result is read as it came, never after it was laid out");
        card.ExitCode.Should().Be(0);
        card.ResultLength.Should().Be(stored.Length);
    }

    [Fact]
    public void A_stored_structured_result_near_the_budget_is_read_as_it_came_and_shown_indented()
    {
        var hours = Enumerable.Repeat(1, (ToolResults.MaxReadLength / 2) - 64).ToArray();
        var stored = JsonSerializer.Serialize(new { city = "Oslo", hours });

        var card = Stored("weather", "{}", stored);

        card.ResultLanguage.Should().Be(ToolResults.JsonLanguage);
        card.Result.Should().StartWith("{\n").And.Contain("\"city\": \"Oslo\"");
    }

    [Fact]
    public void A_stored_result_longer_than_the_read_budget_is_shown_as_it_was_stored()
    {
        var stored = JsonSerializer.Serialize(new string('x', ToolResults.MaxReadLength));

        var card = Stored("search_web", "{}", stored);

        card.Result.Should().Be(stored);
    }

    [Theory]
    [InlineData("\"bad \\x escape\"")]
    [InlineData("\"bad \\uZZZZ escape\"")]
    public void A_text_result_with_an_escape_json_does_not_have_is_shown_as_it_came(string result)
    {
        var call = Call("search_web", "{}") with { Preview = result };

        var card = call.Invoking(called => called.ToCard()).Should().NotThrow().Subject;

        card.Result.Should().Be(result);
    }

    [Fact]
    public void A_text_result_escaping_half_a_surrogate_pair_reads_as_what_it_says()
    {
        var call = Call("search_web", "{}") with { Preview = "\"half \\ud800 a pair\"" };

        var card = call.Invoking(called => called.ToCard()).Should().NotThrow().Subject;

        card.Result.Should().Be("half \ud800 a pair");
    }

    [Fact]
    public void A_stored_result_escaping_half_a_surrogate_pair_never_fails_the_card()
    {
        var stored = "{\"exit_code\":1,\"stdout\":\"half \\ud800 a pair\",\"error\":\"\\udc00 alone\"}";

        var card = FluentActions.Invoking(() => Stored("sandbox_run", "{}", stored)).Should().NotThrow().Subject;

        card.Output.Should().Equal([new ToolOutputPiece("stdout", "half \ud800 a pair")]);
        card.CommandError.Should().Be("\udc00 alone");
        card.ExitCode.Should().Be(1);
    }

    [Fact]
    public void Arguments_escaping_half_a_surrogate_pair_are_shown_as_they_were_written()
    {
        var arguments = "{\"query\":\"\\ud800\"}";
        var call = Call("search_web", arguments) with { Preview = "\"found\"" };

        var card = call.Invoking(called => called.ToCard()).Should().NotThrow().Subject;

        card.Arguments.Should().Be(arguments);
        card.Result.Should().Be("found");
    }

    [Fact]
    public void A_result_with_a_lone_surrogate_character_is_shown_as_valid_text()
    {
        var call = Call("search_web", "{}") with { Preview = "\"half \ud800 a pair\"" };

        var card = call.Invoking(called => called.ToCard()).Should().NotThrow().Subject;

        card.Result.Should().Be("half \ufffd a pair", "a lone surrogate is read as the replacement character");
    }

    [Theory]
    [InlineData("search_web", "\"a whole string\" and then more")]
    [InlineData("weather", "{\"city\":\"Oslo\"} trailing")]
    [InlineData("weather", "[1,2] [3]")]
    [InlineData("sandbox.run", "{\"exit_code\":0,\"stdout\":\"ok\"}x")]
    public void A_whole_value_followed_by_more_text_is_shown_as_it_came_and_nothing_disappears(string tool, string result)
    {
        var call = Call(tool, "{}") with { Preview = result };

        var card = call.Invoking(called => called.ToCard()).Should().NotThrow().Subject;

        card.Result.Should().Be(result);
        card.Output.Should().BeEmpty("text that is not one JSON value is not decoded");
    }

    [Fact]
    public void A_whole_value_followed_only_by_white_space_is_still_decoded()
    {
        var call = Call("search_web", "{}") with { Preview = "\"first\\nsecond\" \r\n\t " };

        call.ToCard().Result.Should().Be("first\nsecond");
    }

    [Fact]
    public void A_stored_value_followed_by_more_text_is_shown_as_it_was_stored()
    {
        var stored = "{\"city\":\"Oslo\"}{\"city\":\"Bergen\"}";

        Stored("weather", "{}", stored).Result.Should().Be(stored);
    }

    [Fact]
    public void Arguments_longer_than_the_read_budget_are_shown_as_stored_and_never_read()
    {
        var arguments = JsonSerializer.Serialize(new { path = "/home/user/big.txt", content = new string('x', ToolResults.MaxReadLength) });
        var stored = JsonSerializer.Serialize(new { path = "/home/user/big.txt", size = 3 });

        var card = FluentActions.Invoking(() => Stored("sandbox_write_file", arguments, stored)).Should().NotThrow().Subject;

        card.Arguments.Should().BeSameAs(arguments, "arguments past the budget are not read, copied, or laid out again");
        card.Summary.Should().BeEmpty("arguments that were not read say nothing");
        card.Result.Should().Contain("\"size\": 3", "the result is read as usual");
    }

    [Fact]
    public void Arguments_that_are_not_json_never_fail_the_card()
    {
        var call = Call("weather", "{not json") with { Preview = "\"sunny\"" };

        var card = call.Invoking(called => called.ToCard()).Should().NotThrow().Subject;

        card.Arguments.Should().Be("{not json");
        card.Result.Should().Be("sunny");
    }

    #endregion

    #region Private Methods

    private static ToolCard Stored(string tool, string arguments, string result)
    {
        var part = new PartView("tool_call", ToolCallId: callId, ToolName: tool, Arguments: arguments);
        var stored = new PartView("tool_result", ToolCallId: callId, Result: result);
        return ToolCards.FromParts(part, stored).ToCard(stored);
    }

    private static ToolCallView Call(string tool, string arguments) =>
        new(callId, tool, tool, arguments, "allow", "completed");

    #endregion
}
