namespace Drover.Domain.Tests;

/// <summary>
/// The two kinds of condition. An exact condition is decided on structure and is guaranteed. A pattern condition
/// looks for something in text: it catches the common forms, raises few false alarms on ordinary work, and costs
/// no more than the evaluation's budget, whatever it is given.
/// </summary>
public sealed class RuleKindsTests
{
    #region Private Fields

    private const int Samples = 2000;

    private const double Ceiling = 0.01;

    private static readonly DateTimeOffset tuesdayNight = new(2026, 11, 10, 23, 30, 0, TimeSpan.Zero);

    private static readonly TimeSpan slack = TimeSpan.FromMilliseconds(500);

    private static readonly int[] primes = [2, 3, 5, 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97, 101, 1009, 7919];

    #endregion

    #region Card Numbers: What Is Found

    [Theory]
    [InlineData("Card 4111 1111 1111 1111 on file.")]
    [InlineData("Card 4111-1111-1111-1111.")]
    [InlineData("4111111111111111")]
    [InlineData("Pay with 5555555555554444 today")]
    [InlineData("Amex 3782 822463 10005")]
    [InlineData("Amex 378282246310005.")]
    [InlineData("Card 4111 1111 1111 1111 12/27 123")]
    [InlineData("Order 42 4111 1111 1111 1111")]
    [InlineData("４１１１ １１１１ １１１１ １１１１")]
    public void A_card_number_in_a_form_cards_are_written_in_is_found_and_removed_whole(string text)
    {
        var outcome = RuleEvaluator.Evaluate([Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Holds(BuiltinPattern.CardNumber))], Reply(text));

        outcome.Should().NotBeNull();
        outcome!.RedactedText.Should().Contain(RuleEvaluator.Redaction).And.NotContain("1111").And.NotContain("4444").And.NotContain("822463").And.NotContain("１１");
    }

    [Theory]
    [InlineData("4111.1111.1111.1111")]
    [InlineData("4111  1111  1111  1111")]
    [InlineData("4 1 1 1 1 1 1 1 1 1 1 1 1 1 1 1")]
    [InlineData("41111111 11111111")]
    [InlineData("4111 1111 1111 1112")]
    [InlineData("1731493815123")]
    [InlineData("3.14159265358979 * 2")]
    public void What_is_not_a_standard_form_or_not_a_card_is_left_alone_and_that_is_the_stated_limit(string text) =>
        RuleEvaluator.Evaluate([Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, Holds(BuiltinPattern.CardNumber))], Reply(text)).Should().BeNull();

    #endregion

    #region Card Numbers: False Alarms

    public static TheoryData<string> OrdinaryData =>
    [
        "timestamps", "unix times", "date ranges", "prices", "invoice prose", "phone numbers", "two phone numbers", "coordinates", "ids", "csv rows", "primes", "arithmetic",
    ];

    [Theory]
    [MemberData(nameof(OrdinaryData))]
    public void Ordinary_data_is_almost_never_taken_for_a_card_number(string kind)
    {
        var random = new Random(89);
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, Holds(BuiltinPattern.CardNumber));
        var alarms = 0;

        for (var sample = 0; sample < Samples; sample++)
        {
            var subject = new RuleSubject
            {
                Checkpoint = RuleCheckpoint.BeforeTool,
                ToolName = "sheet.append",
                ToolKind = ToolSensitivity.Write,
                Json = JsonSerializer.SerializeToElement(new { row = Ordinary(kind, random) }),
                Now = tuesdayNight,
            };
            alarms += RuleEvaluator.Evaluate([rule], subject) is null ? 0 : 1;
        }

        ((double)alarms / Samples).Should().BeLessThanOrEqualTo(Ceiling, $"a starter that blocks ordinary {kind} gets turned off");
    }

    #endregion

    #region One Budget, One Cap

    [Fact]
    public void A_long_sample_against_many_card_rules_costs_no_more_than_the_budget()
    {
        var watch = Stopwatch.StartNew();

        RuleEvaluator.Evaluate(ManyCardRules(RuleCheckpoint.BeforeReply), Reply(Digits(20_000)));

        watch.Elapsed.Should().BeLessThan(RuleBudget.Default + slack, "this took 7.3 seconds");
    }

    [Fact]
    public void A_long_tool_result_against_many_card_rules_costs_no_more_than_the_budget()
    {
        var subject = new RuleSubject { Checkpoint = RuleCheckpoint.AfterTool, ToolName = "web.fetch", ToolKind = ToolSensitivity.Read, Json = JsonSerializer.SerializeToElement(new { page = Digits(100_000) }), Now = tuesdayNight };
        var watch = Stopwatch.StartNew();

        RuleEvaluator.Evaluate(ManyCardRules(RuleCheckpoint.AfterTool), subject);

        watch.Elapsed.Should().BeLessThan(RuleBudget.Default + slack, "this took 36 seconds");
    }

    [Fact]
    public void A_text_over_the_cap_is_not_searched_and_takes_each_rules_effect()
    {
        var text = new string('a', RuleBudget.MaxCharacters + 1);
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate(ManyCardRules(RuleCheckpoint.BeforeReply), Reply(text));

        watch.Elapsed.Should().BeLessThan(RuleBudget.Default + slack, "a megabyte took 300 seconds and then ran out of memory");
        outcome.Should().NotBeNull("what could not be checked is not let through");
        outcome!.Effect.Should().Be(RuleEffect.Block, "a rule that removes text cannot say what is left of a text it did not read");
        outcome.Why.Should().Contain("too much to check");
    }

    [Fact]
    public void Very_many_small_pieces_cost_no_more_than_the_budget()
    {
        var subject = new RuleSubject { Checkpoint = RuleCheckpoint.AfterTool, ToolName = "db.query", ToolKind = ToolSensitivity.Read, Json = JsonSerializer.SerializeToElement(Enumerable.Repeat("ok", 200_000)), Now = tuesdayNight };
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate(ManyCardRules(RuleCheckpoint.AfterTool), subject);

        watch.Elapsed.Should().BeLessThan(RuleBudget.Default + slack, "200,000 tiny leaves took 9.2 seconds");
        outcome.Should().NotBeNull("more leaves than an evaluation looks at is too much to check");
    }

    [Fact]
    public void A_long_text_in_another_script_costs_no_more_than_the_budget()
    {
        var watch = Stopwatch.StartNew();

        RuleEvaluator.Evaluate(ManyCardRules(RuleCheckpoint.BeforeReply), Reply(new string('é', RuleBudget.MaxCharacters)));

        watch.Elapsed.Should().BeLessThan(RuleBudget.Default + slack, "a million characters outside ASCII took 37 seconds");
    }

    [Fact]
    public void A_slow_pattern_on_a_short_text_costs_no_more_than_the_budget()
    {
        RuleCondition[] slow = [.. Enumerable.Repeat(Matches("a+b|a"), 10)];
        ActiveRule[] rules = [.. Enumerable.Range(0, 50).Select(_ => Rule(RuleCheckpoint.BeforeReply, RuleEffect.Redact, slow))];
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate(rules, Reply(new string('a', 4096)));

        watch.Elapsed.Should().BeLessThan(RuleBudget.Default + slack);
        outcome.Should().NotBeNull();
    }

    [Fact]
    public void A_rule_that_ran_out_of_time_says_so_and_does_not_blame_the_name_of_an_input()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Contains("zebra"));
        var subject = new RuleSubject { Checkpoint = RuleCheckpoint.BeforeTool, ToolName = "note.write", ToolKind = ToolSensitivity.Write, Json = JsonSerializer.SerializeToElement(new { note = "hello" }), Now = tuesdayNight };

        var outcome = RuleEvaluator.Evaluate([rule], subject, new RuleBudget(TimeSpan.Zero))!;

        outcome.Effect.Should().Be(RuleEffect.Block);
        outcome.Why.Should().Contain("in time").And.NotContain("name of an input");
    }

    #endregion

    #region Lists On Named Inputs: Exact

    [Theory]
    [InlineData("""{"to":"ann@example.org","To":"mallory@evil.test"}""")]
    [InlineData("""{"TO":"mallory@evil.test"}""")]
    [InlineData("""{"to":"mallory@evil.test","to":"ann@example.org"}""")]
    [InlineData("""{"to":"ann@example.org","to":"mallory@evil.test"}""")]
    [InlineData("""{"to":"ann@example.org","cc":["bob@example.org","mallory@evil.test"]}""")]
    [InlineData("""{"to":[],"bcc":"mallory@evil.test"}""")]
    [InlineData("""{"to":null,"Bcc":"mallory@evil.test"}""")]
    public void A_list_checks_every_occurrence_of_every_input_it_names_whatever_the_case(string arguments)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, ToolIs("email.send"), new RuleCondition { Kind = RuleConditionKind.NotInList, Argument = "to, cc, bcc", Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Call("email.send", arguments)).Should().NotBeNull();
    }

    [Fact]
    public void A_list_lets_through_a_call_whose_named_inputs_are_all_listed()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, ToolIs("email.send"), new RuleCondition { Kind = RuleConditionKind.NotInList, Argument = "to, cc, bcc", Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Call("email.send", """{"To":"ann@example.org","cc":[],"subject":"about mallory@evil.test"}""")).Should().BeNull("only the named inputs are checked: that is what makes it exact");
    }

    #endregion

    #region Addresses Anywhere: A Pattern

    [Theory]
    [InlineData("mallory%40evil.test")]
    [InlineData("mallory!@evil.test")]
    [InlineData("\\\"mal lory\\\"@evil.test")]
    [InlineData("mallory@[203.0.113.5]")]
    [InlineData("mallory@evil。test")]
    [InlineData("mallory@еvil.test")]
    [InlineData("see https://example.org/?next=mallory@evil.test")]
    public void An_address_off_the_list_is_found_in_the_forms_people_use_to_hide_one(string value)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.NotInList, Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Call("email.send", $$"""{"note":"{{value}}"}""")).Should().NotBeNull();
    }

    [Theory]
    [InlineData("npm i @types/node@20.1.0")]
    [InlineData("https://example.org/?to=ann@example.org")]
    [InlineData("Ann Example <ann@example.org>")]
    [InlineData("git log --author=ann@mail.example.org")]
    [InlineData("user@host:~/work$ ls")]
    [InlineData("an @mention and a price @ 4.50")]
    public void What_is_not_an_address_or_is_a_listed_one_inside_other_text_raises_no_alarm(string value)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.NotInList, Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Call("shell.run", JsonSerializer.Serialize(new { command = value }))).Should().BeNull();
    }

    #endregion

    #region What Cannot Be Rewritten

    [Fact]
    public void A_match_in_the_name_of_an_input_blocks_and_nothing_that_matched_is_kept()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Holds(BuiltinPattern.Key));

        var outcome = RuleEvaluator.Evaluate([rule], Call("note.write", """{"sk-sample0000000000000000":"hello","to":"ann@example.org"}"""))!;

        outcome.Effect.Should().Be(RuleEffect.Block);
        outcome.Why.Should().Contain("name of an input");
        outcome.RedactedJson.Should().NotBeNull("the stored copy of the call is what the rules leave");
        outcome.RedactedJson!.Value.GetRawText().Should().NotContain("sk-sample");
    }

    [Fact]
    public void A_word_that_is_only_in_a_name_blocks_and_the_name_is_never_rewritten()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Contains("text"));

        var outcome = RuleEvaluator.Evaluate([rule], Call("note.write", """{"text":"zebra"}"""))!;

        outcome.Effect.Should().Be(RuleEffect.Block);
        outcome.RedactedJson!.Value.GetRawText().Should().NotContain(RuleEvaluator.Redaction);
    }

    [Theory]
    [InlineData("""{"n":1425}""", "42")]
    [InlineData("""{"flag":true}""", "true")]
    [InlineData("""{"gone":null}""", "null")]
    public void A_match_inside_a_number_or_a_flag_blocks_and_does_not_change_its_type(string arguments, string looked)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Contains(looked));

        var outcome = RuleEvaluator.Evaluate([rule], Call("sheet.append", arguments))!;

        outcome.Effect.Should().Be(RuleEffect.Block);
        outcome.Why.Should().Contain("not text");
        outcome.RedactedJson!.Value.GetRawText().Should().NotContain(RuleEvaluator.Redaction);
    }

    [Fact]
    public void Inputs_with_the_same_name_are_each_examined()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, Contains("zebra"));

        RuleEvaluator.Evaluate([rule], Call("note.write", """{"note":"zebra","note":"nothing"}""")).Should().NotBeNull();
    }

    #endregion

    #region Kinds

    [Theory]
    [InlineData(RuleConditionKind.ToolIs, null, false)]
    [InlineData(RuleConditionKind.ToolKind, null, false)]
    [InlineData(RuleConditionKind.TimeIs, null, false)]
    [InlineData(RuleConditionKind.RunExceeds, null, false)]
    [InlineData(RuleConditionKind.NotInList, "to", false)]
    [InlineData(RuleConditionKind.NotInList, null, true)]
    [InlineData(RuleConditionKind.TextContains, null, true)]
    [InlineData(RuleConditionKind.TextMatches, null, true)]
    [InlineData(RuleConditionKind.TextHolds, null, true)]
    public void A_condition_is_exact_or_a_pattern(RuleConditionKind kind, string? argument, bool pattern) =>
        new RuleCondition { Kind = kind, Argument = argument }.IsPattern.Should().Be(pattern);

    #endregion

    #region Helpers

    private static ActiveRule Rule(RuleCheckpoint checkpoint, RuleEffect effect, params RuleCondition[] conditions) =>
        new(Guid.CreateVersion7(), "Sample rule", checkpoint, effect, conditions);

    private static ActiveRule[] ManyCardRules(RuleCheckpoint checkpoint)
    {
        RuleCondition[] cards = [.. Enumerable.Repeat(Holds(BuiltinPattern.CardNumber), 10)];
        return [.. Enumerable.Range(0, 50).Select(_ => Rule(checkpoint, RuleEffect.Redact, cards))];
    }

    private static RuleCondition ToolIs(params string[] tools) => new() { Kind = RuleConditionKind.ToolIs, Tools = tools };

    private static RuleCondition Contains(params string[] values) => new() { Kind = RuleConditionKind.TextContains, Entries = values };

    private static RuleCondition Holds(BuiltinPattern builtin) => new() { Kind = RuleConditionKind.TextHolds, Builtin = builtin };

    private static RuleCondition Matches(string pattern) => new() { Kind = RuleConditionKind.TextMatches, Pattern = pattern };

    private static RuleSubject Reply(string text) => new() { Checkpoint = RuleCheckpoint.BeforeReply, Text = text, Now = tuesdayNight };

    private static RuleSubject Call(string tool, string arguments) =>
        new() { Checkpoint = RuleCheckpoint.BeforeTool, ToolName = tool, ToolKind = ToolSensitivity.Write, Json = JsonElement.Parse(arguments), Now = tuesdayNight };

    // Groups of digits parted by single spaces: the shape that costs a card search the most.
    private static string Digits(int length)
    {
        var text = new StringBuilder(length);

        while (text.Length < length)
        {
            text.Append("4111 1111 1111 1111 ");
        }

        return text.ToString(0, length);
    }

    // One piece of ordinary data of a kind, from a seeded source, so the rates are the same on every run.
    private static string Ordinary(string kind, Random random)
    {
        string Number(int digits) => string.Concat(Enumerable.Range(0, digits).Select(place => (char)('0' + random.Next(place == 0 ? 1 : 0, 10))));
        string Date() => $"{random.Next(2019, 2031)}-{random.Next(1, 13):00}-{random.Next(1, 29):00}";
        string Clock() => $"{random.Next(0, 24):00}:{random.Next(0, 60):00}:{random.Next(0, 60):00}";
        string Price() => $"{random.Next(1, 5000)}.{random.Next(0, 100):00}";
        string Phone() => random.Next(0, 3) switch
        {
            0 => $"+1 {random.Next(200, 990)} {random.Next(200, 990)} {random.Next(0, 10000):0000}",
            1 => $"0{random.Next(30, 90)} {random.Next(1000, 9999)} {random.Next(1000, 9999)}",
            _ => $"({random.Next(200, 990)}) {random.Next(200, 990)}-{random.Next(0, 10000):0000}",
        };

        return kind switch
        {
            "timestamps" => $"{Date()}T{Clock()}Z {Date()} {Clock()}",
            "unix times" => $"{1_600_000_000L + random.Next(0, 200_000_000)} {1_600_000_000_000L + random.NextInt64(0, 200_000_000_000L)}",
            "date ranges" => $"{Date()} {Date()} and {Date()} - {Date()}",
            "prices" => $"${Price()} {Price()} EUR {random.Next(1, 999)},{random.Next(0, 1000):000}.{random.Next(0, 100):00}",
            "invoice prose" => $"Invoice {Number(5)}: {random.Next(1, 20)} x {Price()} = {Price()}, VAT {Price()}, total {Price()} due {Date()}, account {Number(8)} ref {Number(4)} {Number(4)}",
            "phone numbers" => Phone(),
            "two phone numbers" => $"{Phone()} {Phone()}",
            "coordinates" => $"{random.Next(-89, 90)}.{Number(6)}, {random.Next(-179, 180)}.{Number(6)} {random.Next(-89, 90)}.{Number(6)} {random.Next(-179, 180)}.{Number(6)}",
            "ids" => $"{Guid.CreateVersion7()} #{Number(random.Next(4, 13))} order-{Number(6)}-{Number(4)} {Number(10)} {Number(12)}",
            "csv rows" => string.Join(random.Next(0, 2) == 0 ? "," : " ", Enumerable.Range(0, 8).Select(_ => random.Next(0, 10_000).ToString(CultureInfo.InvariantCulture))),
            "primes" => string.Join(' ', Enumerable.Range(0, 12).Select(_ => primes[random.Next(0, primes.Length)])),
            _ => $"{random.Next(1, 10)}.{Number(14)} * {random.Next(2, 99)} = {random.Next(1, 999)}.{Number(12)}",
        };
    }

    #endregion
}
