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

    // The stated bound for one evaluation, whatever it is given: the wall-clock backstop and a second for the rest.
    private static readonly TimeSpan bound = RuleBudget.Backstop + TimeSpan.FromSeconds(1);

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
    public void Many_rules_that_ask_the_same_thing_of_a_long_text_search_it_once()
    {
        var text = Cards(20_000);
        var budget = new RuleBudget(RuleBudget.MaxWork);

        var outcome = RuleEvaluator.Evaluate(ManyCardRules(RuleCheckpoint.BeforeReply), Reply(text), budget)!;

        outcome.RedactedText.Should().Contain(RuleEvaluator.Redaction).And.NotContain("4111", "every card in the sample is found");
        budget.Spent.Should().BeLessThan(text.Length * 20L, "five hundred conditions that ask for a card are one search of the text, one check each, and one rewrite");
    }

    [Fact]
    public void A_long_tool_result_with_cards_in_it_is_searched_within_the_budget()
    {
        var page = Cards(100_000);
        var subject = new RuleSubject { Checkpoint = RuleCheckpoint.AfterTool, ToolName = "web.fetch", ToolKind = ToolSensitivity.Read, Json = JsonSerializer.SerializeToElement(new { page }), Now = tuesdayNight };
        var budget = new RuleBudget(RuleBudget.MaxWork);

        var outcome = RuleEvaluator.Evaluate(ManyCardRules(RuleCheckpoint.AfterTool), subject, budget)!;

        outcome.Effect.Should().Be(RuleEffect.Redact);
        outcome.RedactedJson!.Value.GetRawText().Should().NotContain("4111");
        budget.Spent.Should().BeLessThan(page.Length * 20L);
    }

    [Fact]
    public void Work_is_counted_so_the_same_input_always_gets_the_same_answer()
    {
        var rule = Rule(RuleCheckpoint.BeforeReply, RuleEffect.Block, Contains("zebra"));
        var text = new string('a', 10_000);
        var enough = new RuleBudget(RuleBudget.MaxWork);

        RuleEvaluator.Evaluate([rule], Reply(text), enough).Should().BeNull();
        var needed = enough.Spent;
        needed.Should().BeGreaterThanOrEqualTo(text.Length, "reading a text is work");

        RuleEvaluator.Evaluate([rule], Reply(text), new RuleBudget(needed)).Should().BeNull("exactly the work it takes is enough, every time");
        var lacking = RuleEvaluator.Evaluate([rule], Reply(text), new RuleBudget(needed - 1));
        lacking.Should().NotBeNull("one unit less is not, every time");
        lacking!.Why.Should().Contain("could not be checked");
    }

    [Fact]
    public void A_text_over_the_cap_is_not_searched_and_takes_each_rules_effect()
    {
        var budget = new RuleBudget(RuleBudget.MaxWork);

        var outcome = RuleEvaluator.Evaluate(ManyCardRules(RuleCheckpoint.BeforeReply), Reply(new string('a', RuleBudget.MaxCharacters + 1)), budget);

        outcome.Should().NotBeNull("what could not be checked is not let through");
        outcome!.Effect.Should().Be(RuleEffect.Block, "a rule that removes text cannot say what is left of a text it did not read");
        outcome.Why.Should().Contain("too much to check");
        budget.Spent.Should().Be(0, "a text over the cap is not read at all");
    }

    [Fact]
    public void Very_many_small_pieces_are_too_much_to_check()
    {
        var subject = new RuleSubject { Checkpoint = RuleCheckpoint.AfterTool, ToolName = "db.query", ToolKind = ToolSensitivity.Read, Json = JsonSerializer.SerializeToElement(Enumerable.Repeat("ok", 200_000)), Now = tuesdayNight };

        var outcome = RuleEvaluator.Evaluate(ManyCardRules(RuleCheckpoint.AfterTool), subject);

        outcome.Should().NotBeNull();
        outcome!.Why.Should().Contain("too much to check");
    }

    [Fact]
    public void A_rule_that_ran_out_of_work_says_so_and_does_not_blame_the_name_of_an_input()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Contains("zebra"));
        var subject = new RuleSubject { Checkpoint = RuleCheckpoint.BeforeTool, ToolName = "note.write", ToolKind = ToolSensitivity.Write, Json = JsonSerializer.SerializeToElement(new { note = "hello" }), Now = tuesdayNight };

        var outcome = RuleEvaluator.Evaluate([rule], subject, new RuleBudget(0))!;

        outcome.Effect.Should().Be(RuleEffect.Block);
        outcome.Why.Should().Contain("could not be checked").And.NotContain("name of an input");
    }

    #endregion

    #region No Condition Outruns The Budget

    public static TheoryData<RuleConditionKind, bool> EveryKind
    {
        get
        {
            TheoryData<RuleConditionKind, bool> kinds = [];

            foreach (var kind in Enum.GetValues<RuleConditionKind>())
            {
                kinds.Add(kind, false);
            }

            // A list has two forms: on named inputs, and on every address anywhere in the input.
            kinds.Add(RuleConditionKind.NotInList, true);
            return kinds;
        }
    }

    [Theory]
    [MemberData(nameof(EveryKind))]
    public void No_condition_kind_takes_longer_than_the_bound_on_any_hostile_input(RuleConditionKind kind, bool anywhere)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, [ToolIs("sample.tool"), .. Enumerable.Repeat(Hostile(kind, anywhere), 10)]) with { Effect = kind is RuleConditionKind.TextContains or RuleConditionKind.TextMatches or RuleConditionKind.TextHolds ? RuleEffect.Redact : RuleEffect.Block };
        ActiveRule[] rules = [.. Enumerable.Repeat(rule, 50)];

        foreach (var (shape, arguments) in HostileInputs())
        {
            var subject = new RuleSubject { Checkpoint = RuleCheckpoint.BeforeTool, ToolName = "sample.tool", ToolKind = ToolSensitivity.Write, Json = arguments, Now = tuesdayNight, ToolCalls = 1_000_000 };
            var watch = Stopwatch.StartNew();

            RuleEvaluator.Evaluate(rules, subject);

            watch.Elapsed.Should().BeLessThan(bound, $"fifty rules of ten {kind} conditions on {shape} must end within the bound");
        }
    }

    [Fact]
    public void One_address_rule_on_a_long_run_of_at_signs_is_one_pass_and_does_not_let_it_through()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.NotInList, Entries = ["example.org"] });
        var text = new string('@', 200_000);
        var budget = new RuleBudget(RuleBudget.MaxWork);

        var outcome = RuleEvaluator.Evaluate([rule], Call("note.write", JsonSerializer.Serialize(new { note = text })), budget);

        outcome.Should().NotBeNull("this took 108 seconds and then passed");
        budget.Spent.Should().BeLessThan(text.Length * 4L, "the text is read once and tokenised once");
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
    [InlineData("mallory@evil.test")]
    [InlineData("mallory!@evil.test")]
    [InlineData("mallory@[203.0.113.5]")]
    [InlineData("mallory@evil\u3002test")]
    [InlineData("mallory@\u0435vil.test")]
    [InlineData("see https://example.org/?next=mallory@evil.test")]
    [InlineData("ann\u200B@example.org")]
    [InlineData("git@github.com:org/repo")]
    [InlineData("npm i @types/node@20.1.0")]
    public void A_token_with_an_at_sign_that_is_not_a_listed_address_or_host_is_off_the_list(string value)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.NotInList, Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Call("shell.run", JsonSerializer.Serialize(new { command = value }))).Should().NotBeNull("anything with an @ in it has to be listed: to let one through, list its host");
    }

    [Theory]
    [InlineData("ann@example.org")]
    [InlineData("ANN@Example.ORG")]
    [InlineData("100% done, mail ann@example.org")]
    [InlineData("mailto:ann@example.org")]
    [InlineData("https://example.org/?to=ann@example.org")]
    [InlineData("Ann Example <ann@example.org>")]
    [InlineData("git log --author=ann@mail.example.org")]
    [InlineData("Write to ann@example.org.")]
    [InlineData("no address here, 50% off")]
    public void A_listed_address_inside_other_text_raises_no_alarm(string value)
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.NotInList, Entries = ["example.org"] });

        RuleEvaluator.Evaluate([rule], Call("shell.run", JsonSerializer.Serialize(new { command = value }))).Should().BeNull();
    }

    [Fact]
    public void A_listed_address_lists_that_address_and_no_other_at_its_host()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, new RuleCondition { Kind = RuleConditionKind.NotInList, Entries = ["sam@example.org"] });

        RuleEvaluator.Evaluate([rule], Call("email.send", """{"note":"mailto:Sam@Example.org"}""")).Should().BeNull();
        RuleEvaluator.Evaluate([rule], Call("email.send", """{"note":"x.sam@example.org"}""")).Should().NotBeNull();
        RuleEvaluator.Evaluate([rule], Call("email.send", """{"note":"ann@example.org"}""")).Should().NotBeNull();
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
    public void Inputs_that_share_a_name_cannot_be_rewritten_so_a_rule_that_would_blocks()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Redact, Contains("zebra"));

        var outcome = RuleEvaluator.Evaluate([rule], Call("note.write", """{"a":"zebra","a":"keep"}"""))!;

        outcome.Effect.Should().Be(RuleEffect.Block, "rewriting would keep only one of the two");
        outcome.Why.Should().Contain("share a name");
        outcome.RedactedJson!.Value.GetRawText().Should().NotContain("zebra");
    }

    [Fact]
    public void A_rule_that_blocks_on_a_secret_in_a_name_keeps_nothing_of_the_call()
    {
        var rule = Rule(RuleCheckpoint.BeforeTool, RuleEffect.Block, Holds(BuiltinPattern.Key));

        var outcome = RuleEvaluator.Evaluate([rule], Call("note.write", """{"sk-sample0000000000000000":"hello"}"""))!;

        outcome.Effect.Should().Be(RuleEffect.Block);
        outcome.RedactedJson.Should().NotBeNull("a name cannot be removed from a call, so the stored copy of the call is emptied");
        outcome.RedactedJson!.Value.GetRawText().Should().NotContain("sk-sample");
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

    // Card after card, each standing alone, so every one of them is found.
    private static string Cards(int length)
    {
        var text = new StringBuilder(length);

        while (text.Length < length)
        {
            text.Append("4111 1111 1111 1111 and ");
        }

        return text.ToString(0, length - (length % 24));
    }

    // A condition of each kind, written to be as costly as that kind can be. A kind that is not here fails the test.
    private static RuleCondition Hostile(RuleConditionKind kind, bool anywhere) => kind switch
    {
        RuleConditionKind.ToolIs => ToolIs("sample.tool"),
        RuleConditionKind.ToolKind => new() { Kind = kind, ToolKinds = [ToolSensitivity.Write] },
        RuleConditionKind.TextContains => Contains("@", "%", ".", "4", "a", "\"", "<", " ", "aa", "@a"),
        RuleConditionKind.TextMatches => Matches("a+b|a|@+%|4+5"),
        RuleConditionKind.TextHolds => Holds(BuiltinPattern.CardNumber),
        RuleConditionKind.NotInList => new() { Kind = kind, Argument = anywhere ? null : "text, list, deep", Entries = ["example.org"] },
        RuleConditionKind.TimeIs => new() { Kind = kind, From = "00:00", To = "23:59", TimeZone = "UTC" },
        RuleConditionKind.RunExceeds => new() { Kind = kind, Counter = RunCounter.ToolCalls, Limit = 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Add the new condition kind to the hostile inputs test."),
    };

    // Inputs built to make a scanner do the most work: one character repeated, pairs, deep nesting, tiny leaves.
    private static IEnumerable<(string Shape, JsonElement Arguments)> HostileInputs()
    {
        int[] sizes = [1_000, 60_000, RuleBudget.MaxCharacters - 16];
        string[] units = ["@", "%", ".", "4", "a", "\"", "<", " ", "@a", "4 ", "a@", "%4", "4111 "];

        foreach (var size in sizes)
        {
            foreach (var unit in units)
            {
                var text = string.Concat(Enumerable.Repeat(unit, size / unit.Length));
                yield return ($"{size:N0} characters of '{unit}'", JsonSerializer.SerializeToElement(new { text }));
            }
        }

        yield return ("many tiny leaves", JsonSerializer.SerializeToElement(new { list = Enumerable.Repeat("a@", RuleBudget.MaxLeaves - 8) }));
        yield return ("more leaves than the cap", JsonSerializer.SerializeToElement(new { list = Enumerable.Repeat("4", 200_000) }));
        yield return ("deep nesting", JsonElement.Parse($"{{\"deep\":{new string('[', 60)}\"a@4%\"{new string(']', 60)}}}"));
    }

    // One piece of ordinary data of a kind, from a seeded source, so the rates are the same on every run.
    private static string Ordinary(string kind, Random random)
    {
        string Number(int digits) => string.Concat(Enumerable.Range(0, digits).Select(place => (char)('0' + random.Next(place == 0 ? 1 : 0, 10))));
        string Uuid()
        {
            var bytes = new byte[16];
            random.NextBytes(bytes);
            return new Guid(bytes).ToString();
        }

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
            "ids" => $"{Uuid()} #{Number(random.Next(4, 13))} order-{Number(6)}-{Number(4)} {Number(10)} {Number(12)}",
            "csv rows" => string.Join(random.Next(0, 2) == 0 ? "," : " ", Enumerable.Range(0, 8).Select(_ => random.Next(0, 10_000).ToString(CultureInfo.InvariantCulture))),
            "primes" => string.Join(' ', Enumerable.Range(0, 12).Select(_ => primes[random.Next(0, primes.Length)])),
            _ => $"{random.Next(1, 10)}.{Number(14)} * {random.Next(2, 99)} = {random.Next(1, 999)}.{Number(12)}",
        };
    }

    #endregion
}
