namespace Drover.Domain.Tests;

/// <summary>
/// The one evaluator of bot rules: which rule applies, which wins, what a redaction leaves, and that nothing it
/// returns holds the text that matched. Every value here is a sample.
/// </summary>
public sealed class RuleEvaluatorTests
{
    #region Private Fields

    private static readonly DateTimeOffset tuesdayNight = new(2026, 11, 10, 23, 30, 0, TimeSpan.Zero);

    private static readonly string[] twoRecipients = ["a@example.org", "b@outside.test"];

    #endregion

    #region Conditions

    [Fact]
    public void A_rule_with_no_condition_always_applies_at_its_checkpoint_and_nowhere_else()
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Block);

        RuleEvaluator.Evaluate([rule], Reply("Anything")).Should().NotBeNull();
        RuleEvaluator.Evaluate([rule], Message("Anything")).Should().BeNull();
    }

    [Fact]
    public void Every_condition_of_a_rule_must_hold()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, ToolIs("email.send"), Contains(null, "urgent"));

        RuleEvaluator.Evaluate([rule], Tool("email.send", new { body = "This is URGENT." })).Should().NotBeNull("text is found whatever its case");
        RuleEvaluator.Evaluate([rule], Tool("email.send", new { body = "Whenever." })).Should().BeNull();
        RuleEvaluator.Evaluate([rule], Tool("notes.write", new { body = "urgent" })).Should().BeNull();
    }

    [Theory]
    [InlineData(ToolSensitivity.Write, true)]
    [InlineData(ToolSensitivity.Critical, true)]
    [InlineData(ToolSensitivity.Read, false)]
    public void A_rule_can_be_about_what_kind_of_tool_it_is(ToolSensitivity kind, bool applies)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Ask, new RuleCondition { Kind = RuleConditionKind.ToolKind, ToolKinds = [ToolSensitivity.Write, ToolSensitivity.Critical] });

        (RuleEvaluator.Evaluate([rule], Tool("any.tool", new { }, kind)) is not null).Should().Be(applies);
    }

    [Fact]
    public void A_condition_on_one_argument_looks_only_there_and_through_lists_and_objects_beneath_it()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, Contains("to", "outside"));

        RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = twoRecipients, body = "x" })).Should().NotBeNull();
        RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = new { address = "b@OUTSIDE.test" } })).Should().NotBeNull();
        RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = "a@example.org", body = "outside" })).Should().BeNull("the body is another argument");
    }

    [Theory]
    [InlineData("a@example.org", false)]
    [InlineData("A@Example.ORG", false)]
    [InlineData("a@mail.example.org", false)]
    [InlineData("example.org", false)]
    [InlineData("a@example.org.attacker.test", true)]
    [InlineData("a@notexample.org", true)]
    public void A_value_is_on_a_list_as_written_or_under_a_listed_domain(string recipient, bool applies)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Ask, new RuleCondition { Kind = RuleConditionKind.NotInList, Argument = "to", Values = ["example.org"] });

        (RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = new[] { "b@example.org", recipient } })) is not null).Should().Be(applies);
    }

    [Theory]
    [InlineData(BuiltinPattern.EmailAddress, "Write to sam@example.org today.", true)]
    [InlineData(BuiltinPattern.EmailAddress, "No address here.", false)]
    [InlineData(BuiltinPattern.CardNumber, "Card 4111 1111 1111 1111 on file.", true)]
    [InlineData(BuiltinPattern.CardNumber, "Card 4111-1111-1111-1111.", true)]
    [InlineData(BuiltinPattern.CardNumber, "Order 4111 1111 1111 1112 shipped.", false)]
    [InlineData(BuiltinPattern.CardNumber, "Call 555 0100.", false)]
    [InlineData(BuiltinPattern.Key, "Use sk-sample0000000000000000 for this.", true)]
    [InlineData(BuiltinPattern.Key, "Authorization: Bearer sampletoken0000000000000", true)]
    [InlineData(BuiltinPattern.Key, "The key to success is practice.", false)]
    public void Built_in_patterns_find_their_kind_and_a_card_number_must_pass_its_check_digit(BuiltinPattern builtin, string text, bool applies)
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Block, Holds(builtin));

        (RuleEvaluator.Evaluate([rule], Reply(text)) is not null).Should().Be(applies);
    }

    [Fact]
    public void A_number_in_a_tools_result_is_text_too()
    {
        var rule = Rule(RuleCheckpoint.AfterTool, RuleEffect.Block, Holds(BuiltinPattern.CardNumber));

        RuleEvaluator.Evaluate([rule], Result(new { card = 4111111111111111 })).Should().NotBeNull();
    }

    [Theory]
    [InlineData("22:00", "07:00", "Europe/Berlin", true, "00:30 on Wednesday in Berlin is inside a night that began on Tuesday")]
    [InlineData("22:00", "07:00", "UTC", true, "23:30 on Tuesday")]
    [InlineData("09:00", "17:00", "UTC", false, "outside office hours")]
    [InlineData("23:00", "23:30", "UTC", false, "the end of the span is not part of it")]
    public void Hours_are_read_in_the_conditions_own_zone_and_may_cross_midnight(string from, string to, string zone, bool applies, string because)
    {
        var rule = Rule(RuleCheckpoint.MessageArrives, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.TimeIs, From = from, To = to, TimeZone = zone, Weekdays = [DayOfWeek.Tuesday] });

        (RuleEvaluator.Evaluate([rule], Message("Hello")) is not null).Should().Be(applies, because);
    }

    [Theory]
    [InlineData(RunCounter.ToolCalls, 5, 5, 0, false)]
    [InlineData(RunCounter.ToolCalls, 5, 6, 0, true)]
    [InlineData(RunCounter.Tokens, 1000, 0, 1001, true)]
    [InlineData(RunCounter.Tokens, 1000, 99, 1000, false)]
    public void A_run_that_has_used_more_than_a_number_meets_the_condition(RunCounter counter, long limit, int toolCalls, long tokens, bool applies)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Stop, new RuleCondition { Kind = RuleConditionKind.RunExceeds, Counter = counter, Limit = limit });
        var subject = Tool("any.tool", new { }) with { ToolCalls = toolCalls, Tokens = tokens };

        (RuleEvaluator.Evaluate([rule], subject) is not null).Should().Be(applies);
    }

    #endregion

    #region Which Rule Wins

    [Fact]
    public void The_strictest_effect_wins_and_among_equals_the_first_in_order()
    {
        var redact = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Contains(null, "x")) with { Name = "redact" };
        var ask = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Ask) with { Name = "ask" };
        var block = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block) with { Name = "block" };
        var blockToo = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block) with { Name = "block too" };
        var stop = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Stop) with { Name = "stop" };
        var subject = Tool("any.tool", new { text = "x" });

        RuleEvaluator.Evaluate([redact, ask, block, blockToo, stop], subject)!.Rule.Name.Should().Be("stop");
        RuleEvaluator.Evaluate([redact, ask, block, blockToo], subject)!.Rule.Name.Should().Be("block");
        RuleEvaluator.Evaluate([redact, ask, blockToo, block], subject)!.Rule.Name.Should().Be("block too");
        RuleEvaluator.Evaluate([redact, ask], subject)!.Rule.Name.Should().Be("ask");
        RuleEvaluator.Evaluate([redact], subject)!.Effect.Should().Be(RuleEffect.Redact);
        RuleEvaluator.Evaluate([redact, ask, block], subject)!.Applied.Select(rule => rule.Name).Should().Equal("block", "redact", "ask");
    }

    [Fact]
    public void A_rule_only_narrows_no_rule_gives_no_outcome_and_no_outcome_ever_allows()
    {
        RuleEvaluator.Evaluate([], Tool("any.tool", new { })).Should().BeNull();
        Enum.GetNames<RuleEffect>().Should().Equal(["Redact", "Ask", "Block", "Stop"], "there is no effect that permits anything");
    }

    #endregion

    #region Redaction

    [Fact]
    public void Redaction_replaces_every_match_of_every_applying_rule_and_leaves_the_rest()
    {
        var cards = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Holds(BuiltinPattern.CardNumber));
        var names = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Contains(null, "Project Lantern"));

        var outcome = RuleEvaluator.Evaluate([cards, names], Reply("project lantern bills 4111 1111 1111 1111 and 4111111111111111 monthly."))!;

        outcome.RedactedText.Should().Be($"{RuleEvaluator.Redaction} bills {RuleEvaluator.Redaction} and {RuleEvaluator.Redaction} monthly.");
    }

    [Fact]
    public void Redaction_reaches_into_arguments_keeps_their_shape_and_respects_a_named_argument()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Holds(BuiltinPattern.Key) with { Argument = "body" });
        var arguments = new { to = "sk-sample0000000000000000", body = new[] { "Key sk-sample0000000000000000 inside.", "Plain." }, count = 2 };

        var redacted = RuleEvaluator.Evaluate([rule], Tool("email.send", arguments))!.RedactedJson!.Value;

        redacted.GetProperty("to").GetString().Should().Be("sk-sample0000000000000000", "the rule names another argument");
        redacted.GetProperty("body")[0].GetString().Should().Be($"Key {RuleEvaluator.Redaction} inside.");
        redacted.GetProperty("body")[1].GetString().Should().Be("Plain.");
        redacted.GetProperty("count").GetInt32().Should().Be(2);
    }

    [Fact]
    public void Overlapping_matches_are_replaced_once()
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Contains(null, "secret plan", "plan nine"));

        RuleEvaluator.Evaluate([rule], Reply("The secret plan nine works."))!.RedactedText.Should().Be($"The {RuleEvaluator.Redaction} works.");
    }

    [Fact]
    public void An_outcome_says_why_in_words_and_never_holds_the_text_that_matched()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, ToolIs("email.send"), Holds(BuiltinPattern.CardNumber), Contains("body", "Project Lantern"));

        var outcome = RuleEvaluator.Evaluate([rule], Tool("email.send", new { body = "Project Lantern 4111 1111 1111 1111" }))!;

        outcome.Why.Should().Be("the tool is email.send, and the text holds a card number, and the argument body contains text the rule names");
        JsonSerializer.Serialize(new { outcome.Why, outcome.Effect }).Should().NotContain("4111").And.NotContain("Lantern");
    }

    #endregion

    #region Patterns

    [Fact]
    public void A_written_pattern_matches_and_redacts()
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Matches(@"INV-\d{4}"));

        RuleEvaluator.Evaluate([rule], Reply("See INV-0042 and INV-7."))!.RedactedText.Should().Be($"See {RuleEvaluator.Redaction} and INV-7.");
    }

    [Fact]
    public void A_pattern_built_to_stall_a_backtracking_engine_finishes_at_once()
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Block, Matches("(a+)+$"));
        var hostile = $"{new string('a', 50_000)}!";
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate([rule], Reply(hostile));

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the engine cannot backtrack, and a match that runs out of time ends");
        outcome.Should().BeNull("the text does not end in an a");
    }

    [Fact]
    public void A_pattern_that_cannot_be_read_at_run_time_applies_so_a_rule_fails_closed()
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Matches("(?<=a)b"));

        var outcome = RuleEvaluator.Evaluate([rule], Reply("ab"))!;

        outcome.RedactedText.Should().Be(RuleEvaluator.Redaction, "nothing of a text is let through a rule that cannot be evaluated");
    }

    #endregion

    #region Helpers

    private static ActiveRule Rule(RuleCheckpoint checkpoint, RuleEffect effect, params RuleCondition[] conditions) =>
        new(Guid.CreateVersion7(), "Sample rule", checkpoint, effect, conditions);

    private static RuleCondition ToolIs(params string[] tools) => new() { Kind = RuleConditionKind.ToolIs, Tools = tools };

    private static RuleCondition Contains(string? argument, params string[] values) => new() { Kind = RuleConditionKind.TextContains, Argument = argument, Values = values };

    private static RuleCondition Holds(BuiltinPattern builtin) => new() { Kind = RuleConditionKind.TextHolds, Builtin = builtin };

    private static RuleCondition Matches(string pattern) => new() { Kind = RuleConditionKind.TextMatches, Pattern = pattern };

    private static RuleSubject Tool(string name, object arguments, ToolSensitivity kind = ToolSensitivity.Write) =>
        new() { Checkpoint = RuleCheckpoint.BeforeTool, ToolName = name, ToolKind = kind, Json = JsonSerializer.SerializeToElement(arguments), Now = tuesdayNight };

    private static RuleSubject Result(object result) =>
        new() { Checkpoint = RuleCheckpoint.AfterTool, ToolName = "any.tool", ToolKind = ToolSensitivity.Read, Json = JsonSerializer.SerializeToElement(result), Now = tuesdayNight };

    private static RuleSubject Reply(string text) => new() { Checkpoint = RuleCheckpoint.BeforeReply, Text = text, Now = tuesdayNight };

    private static RuleSubject Message(string text) => new() { Checkpoint = RuleCheckpoint.MessageArrives, Text = text, Now = tuesdayNight };

    #endregion
}
