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
