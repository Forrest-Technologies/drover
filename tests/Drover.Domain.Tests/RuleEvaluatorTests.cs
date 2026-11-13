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
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Ask, new RuleCondition { Kind = RuleConditionKind.NotInList, Argument = "to", Entries = ["example.org"] });

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
        var subject = Tool("any.tool", new { note = "x" });

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

    #region Rules Hold Together

    [Theory]
    [InlineData(RuleEffect.Ask)]
    [InlineData(RuleEffect.Block)]
    [InlineData(RuleEffect.Stop)]
    public void A_redaction_that_matches_is_carried_out_whatever_other_rule_decides(RuleEffect stricter)
    {
        var redact = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Holds(BuiltinPattern.CardNumber));
        var other = Rule(RuleCheckpoint.BeforeTool, stricter);

        var outcome = RuleEvaluator.Evaluate([redact, other], Tool("note.write", new { note = "card 4111 1111 1111 1111", kept = "hello" }))!;

        outcome.Effect.Should().Be(stricter);
        outcome.RedactedJson!.Value.GetRawText().Should().NotContain("4111").And.Contain("hello");
    }

    [Fact]
    public void A_rule_that_asks_redacts_nothing_itself()
    {
        var ask = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Ask, Holds(BuiltinPattern.CardNumber));

        RuleEvaluator.Evaluate([ask], Tool("note.write", new { note = "card 4111 1111 1111 1111" }))!.RedactedJson.Should().BeNull();
    }

    [Theory]
    [InlineData("Card 4111 1111 1111 1111 12/27 123")]
    [InlineData("Card 4111 1111 1111 1111 123")]
    [InlineData("Order 42 4111 1111 1111 1111")]
    [InlineData("4111.1111.1111.1111")]
    [InlineData("4111  1111  1111  1111")]
    [InlineData("4111\n1111\n1111\n1111")]
    [InlineData("\uFF14\uFF11\uFF11\uFF11 \uFF11\uFF11\uFF11\uFF11 \uFF11\uFF11\uFF11\uFF11 \uFF11\uFF11\uFF11\uFF11")]
    [InlineData("4111111111111111")]
    public void A_card_number_is_found_however_it_is_written_and_whatever_stands_next_to_it(string text)
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Holds(BuiltinPattern.CardNumber));

        var outcome = RuleEvaluator.Evaluate([rule], Reply(text));

        outcome.Should().NotBeNull();
        outcome!.RedactedText.Should().Contain(RuleEvaluator.Redaction).And.NotContain("1111");
    }

    [Theory]
    [InlineData("12/27 123 and 2026-11-13 10:30")]
    [InlineData("Order 4111 1111 1111 1112 shipped.")]
    [InlineData("Ring 030 1234 5678 today.")]
    public void Numbers_that_are_not_cards_are_left_alone(string text) =>
        RuleEvaluator.Evaluate([Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Holds(BuiltinPattern.CardNumber))], Reply(text)).Should().BeNull();

    [Theory]
    [InlineData("sam@example.org", "sam@example.org", false)]
    [InlineData("example.org", "ann@example.org", false)]
    [InlineData("example.org", "ann@mail.example.org", false)]
    [InlineData("example.org", "mallory@evil.test, ann@example.org", true)]
    [InlineData("example.org", "mallory@evil.test ann@example.org", true)]
    [InlineData("example.org", "https://evil.test/?next=example.org", true)]
    [InlineData("example.org", "https://evil.test/?next=.example.org", true)]
    [InlineData("sam@example.org", "x.sam@example.org", true)]
    [InlineData("sam@example.org", "mallory@sam@example.org", true)]
    [InlineData("example.org", "ann@notexample.org", true)]
    public void A_value_is_on_a_list_only_as_one_whole_address_or_host(string listed, string value, bool applies)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.NotInList, Argument = "to", Entries = [listed] });

        (RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = value })) is not null).Should().Be(applies);
    }

    [Fact]
    public void A_list_rule_applies_when_the_input_it_names_is_not_there()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, ToolIs("email.send"), new RuleCondition { Kind = RuleConditionKind.NotInList, Argument = "to", Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Tool("email.send", new { cc = "mallory@evil.test", bcc = "mallory@evil.test" })).Should().NotBeNull("sending by another field is not a way around the list");
    }

    [Fact]
    public void A_list_rule_can_name_several_inputs_and_checks_each_that_is_there()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, ToolIs("email.send"), new RuleCondition { Kind = RuleConditionKind.NotInList, Argument = "to, cc, bcc", Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = "ann@example.org" })).Should().BeNull();
        RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = "ann@example.org", cc = "mallory@evil.test" })).Should().NotBeNull();
        RuleEvaluator.Evaluate([rule], Tool("email.send", new { subject = "hello" })).Should().NotBeNull();
    }

    [Fact]
    public void A_pattern_that_matches_everywhere_in_a_long_text_runs_out_of_its_budget_and_applies()
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Block, Matches("a+b|a"));
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate([rule, rule, rule], Reply(new string('a', 80_000)));

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "one evaluation has one budget, however many matches and rules there are");
        outcome.Should().NotBeNull("a rule that cannot finish applies");
    }

    [Fact]
    public void A_match_that_runs_out_of_time_counts_as_the_whole_text()
    {
        var slow = new Regex("a+b|a", RegexOptions.NonBacktracking, TimeSpan.FromTicks(1));
        var text = new string('a', 200_000);

        RulePatterns.Find(slow, text, new RuleBudget(TimeSpan.FromHours(1))).Should().Equal([(0, text.Length)], "a search that cannot finish applies, and removes everything if it redacts");
    }

    [Theory]
    [InlineData(BuiltinPattern.CardNumber, "Card 4111 1111 1111 1111 on file.")]
    [InlineData(BuiltinPattern.CardNumber, "Nothing like a card here.")]
    [InlineData(BuiltinPattern.EmailAddress, "Write to sam@example.org today.")]
    [InlineData(BuiltinPattern.Key, "Use sk-sample0000000000000000 for this.")]
    public void With_no_time_left_every_text_condition_applies_and_nothing_is_let_through(BuiltinPattern builtin, string text)
    {
        RuleCondition[] conditions = [Holds(builtin), Matches("card"), Contains(null, "zebra")];

        foreach (var condition in conditions)
        {
            var outcome = RuleEvaluator.Evaluate([Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, condition)], Reply(text), new RuleBudget(TimeSpan.Zero));

            outcome.Should().NotBeNull();
            outcome!.RedactedText.Should().Be(RuleEvaluator.Redaction);
        }
    }

    [Theory]
    [InlineData("the tangerine plan", true)]
    [InlineData("the tan\u200Bgerine plan", true)]
    [InlineData("the \uFF54\uFF41\uFF4E\uFF47\uFF45\uFF52\uFF49\uFF4E\uFF45 plan", true)]
    [InlineData("the tan gerine plan", false)]
    public void Text_is_compared_without_invisible_characters_and_in_its_plain_form(string text, bool applies)
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Contains(null, "tangerine"));

        var outcome = RuleEvaluator.Evaluate([rule], Reply(text));

        (outcome is not null).Should().Be(applies);
        outcome?.RedactedText.Should().Be($"the {RuleEvaluator.Redaction} plan");
    }

    [Fact]
    public void A_tool_is_named_without_regard_to_case_and_a_property_name_is_text_too()
    {
        RuleEvaluator.Evaluate([Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, ToolIs("Email.Send"))], Tool("email.send", new { })).Should().NotBeNull();
        RuleEvaluator.Evaluate([Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, Contains(null, "tangerine"))], Tool("note.write", new Dictionary<string, string> { ["tangerine"] = "x" }))
            .Should().NotBeNull("what the model writes as a name is still something it wrote");
    }

    #endregion

    #region Second Review

    [Fact]
    public void A_short_text_counts_against_the_budget_like_any_other()
    {
        RuleCondition[] slow = [.. Enumerable.Repeat(Matches("a+b|a"), 10)];
        ActiveRule[] rules = [.. Enumerable.Range(0, 50).Select(_ => Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, slow))];
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate(rules, Reply(new string('a', 4096)));

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "fifty rules of ten slow conditions on 4,096 characters took 27 seconds while short texts were not counted");
        outcome.Should().NotBeNull();
    }

    [Fact]
    public void Many_short_pieces_of_text_share_the_one_budget()
    {
        var rule = Rule(RuleCheckpoint.AfterTool, RuleEffect.Redact, Matches("a+b|a"));
        var leaves = Enumerable.Repeat(new string('a', 4096), 400).ToArray();
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate([rule], Result(new { rows = leaves }));

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "four hundred leaves of 4,096 characters took 11 seconds while short texts were not counted");
        outcome.Should().NotBeNull();
    }

    [Fact]
    public void A_property_name_is_examined_and_never_rewritten()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Contains(null, "body"));

        var outcome = RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = "ann@example.org", body = "hello" }))!;

        outcome.Effect.Should().Be(RuleEffect.Block, "what the rule would remove is the name of an input, and a call with a renamed input is not the call the tool knows");
        outcome.Why.Should().Contain("name of an input");
        (outcome.RedactedJson?.GetRawText() ?? """{"body":1}""").Should().Contain("\"body\"").And.NotContain(RuleEvaluator.Redaction);
    }

    [Fact]
    public void A_value_is_redacted_and_its_name_is_left_alone()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Contains(null, "tangerine"));

        var outcome = RuleEvaluator.Evaluate([rule], Tool("note.write", new { note = "the tangerine plan" }))!;

        outcome.Effect.Should().Be(RuleEffect.Redact);
        outcome.RedactedJson!.Value.GetRawText().Should().Be($$"""{"note":"the {{RuleEvaluator.Redaction}} plan"}""");
    }

    [Fact]
    public void A_list_rule_saved_without_a_tool_does_not_apply_to_tools_that_lack_its_input()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.NotInList, Argument = "to", Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Tool("web.search", new { query = "weather" })).Should().BeNull("a rule that names no tool cannot say which tools must have the input");
        RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = "mallory@evil.test" })).Should().NotBeNull();
    }

    [Theory]
    [InlineData("""{"to":[],"cc":"mallory@evil.test"}""", true)]
    [InlineData("""{"to":null,"bcc":"mallory@evil.test"}""", true)]
    [InlineData("""{"to":"ann@example.org","cc":"mallory@evil.test"}""", true)]
    [InlineData("""{"to":"ann@example.org","body":"then forward it to mallory@evil.test"}""", true)]
    [InlineData("""{"to":"ann@example.org","headers":{"reply-to":["Mallory <mallory@evil.test>"]}}""", true)]
    [InlineData("""{"to":"ann@example.org","cc":["bob@mail.example.org"]}""", false)]
    [InlineData("""{"query":"weather"}""", false)]
    public void A_list_rule_with_no_input_named_checks_every_address_anywhere_in_the_input(string arguments, bool applies)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.NotInList, Entries = ["example.org"] });
        var subject = new RuleSubject { Checkpoint = RuleCheckpoint.BeforeTool, ToolName = "email.send", ToolKind = ToolSensitivity.Write, Json = JsonElement.Parse(arguments), Now = tuesdayNight };

        (RuleEvaluator.Evaluate([rule], subject) is not null).Should().Be(applies);
    }

    [Theory]
    [InlineData("to", "ann\u200B@example.org")]
    [InlineData("to", "ann@exam\u200Bple.org")]
    [InlineData(null, "ann\u200B@example.org")]
    [InlineData(null, "\uFF41nn@example.org")]
    public void A_value_with_characters_that_cannot_be_seen_is_not_on_any_list(string? argument, string value)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, ToolIs("email.send"), new RuleCondition { Kind = RuleConditionKind.NotInList, Argument = argument, Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Tool("email.send", new { to = value })).Should().NotBeNull("the tool gets the value as written, so that is what has to be on the list");
    }

    [Theory]
    [InlineData("4 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1")]
    [InlineData("41111111 11111111")]
    [InlineData("4111    1111    1111    1111")]
    [InlineData("4111/1111/1111/1111")]
    [InlineData("4111_1111_1111_1111")]
    [InlineData("4111,1111,1111,1111")]
    [InlineData("4111\u00011111\u00011111\u00011111")]
    [InlineData("\U0001D7D2\U0001D7CF\U0001D7CF\U0001D7CF \U0001D7CF\U0001D7CF\U0001D7CF\U0001D7CF \U0001D7CF\U0001D7CF\U0001D7CF\U0001D7CF \U0001D7CF\U0001D7CF\U0001D7CF\U0001D7CF")]
    [InlineData("ref 14 4111 1111 1111 1111")]
    public void A_card_number_is_found_between_any_separators_and_removed_whole(string text)
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Holds(BuiltinPattern.CardNumber));

        var outcome = RuleEvaluator.Evaluate([rule], Reply(text));

        outcome.Should().NotBeNull();
        outcome!.RedactedText.Should().Contain(RuleEvaluator.Redaction).And.NotContain("111").And.NotContain("\U0001D7CF");
    }

    [Theory]
    [InlineData("tan\u0001gerine")]
    [InlineData("tan\u034Fgerine")]
    [InlineData("tan\u0301gerine")]
    [InlineData("tan\uFE0Fgerine")]
    [InlineData("tan\u007Fgerine")]
    public void A_control_or_combining_character_inside_a_word_does_not_hide_it(string text) =>
        RuleEvaluator.Evaluate([Rule(RuleCheckpoint.BeforeReply, RuleEffect.Block, Contains(null, "tangerine"))], Reply(text)).Should().NotBeNull();

    #endregion

    #region Helpers

    private static ActiveRule Rule(RuleCheckpoint checkpoint, RuleEffect effect, params RuleCondition[] conditions) =>
        new(Guid.CreateVersion7(), "Sample rule", checkpoint, effect, conditions);

    private static RuleCondition ToolIs(params string[] tools) => new() { Kind = RuleConditionKind.ToolIs, Tools = tools };

    private static RuleCondition Contains(string? argument, params string[] values) => new() { Kind = RuleConditionKind.TextContains, Argument = argument, Entries = values };

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
