namespace Drover.Ui.Tests;

/// <summary>Rules in words: the sentence a rule reads back as, the closed section's line, and what the builder offers and sends.</summary>
public sealed class RuleWordsTests
{
    #region Sentences

    public static TheoryData<string, string, RuleConditionBody[], string> Sentences => new()
    {
        { RuleWords.BeforeTool, "ask", [new("tool_kind") { ToolKinds = ["write", "critical"] }], "When a tool is about to run, if the tool writes or is critical, then ask a person first." },
        { RuleWords.BeforeTool, "block", [new("tool_is") { Tools = ["sample.send", "sample.post"] }], "When a tool is about to run, if the tool is sample.send or sample.post, then do not run it." },
        { RuleWords.BeforeTool, "block", [new("not_in_list") { Argument = "to", Entries = ["example.org", "example.com"] }], "When a tool is about to run, if the input \"to\" is not one of example.org, example.com, then do not run it." },
        { RuleWords.AfterTool, "redact", [new("text_holds") { Builtin = "card_number" }], "When a tool has answered, if the tool's answer holds a card number, then remove what matched." },
        { RuleWords.AfterTool, "block", [new("text_matches") { Pattern = "INV-\\d+" }], "When a tool has answered, if the tool's answer matches the pattern INV-\\d+, then keep the answer from the bot." },
        { RuleWords.BeforeReply, "block", [new("text_contains") { Entries = ["one", "two"] }], "When the bot is about to reply, if the reply contains \"one\" or \"two\", then do not send it." },
        { RuleWords.MessageArrives, "block", [new("time_is") { From = "22:00", To = "07:00", TimeZone = "Europe/Berlin", Weekdays = ["saturday", "sunday"] }], "When a message arrives, if it is between 22:00 and 07:00 on Saturday, Sunday (Europe/Berlin), then do not give it to the bot." },
        { RuleWords.BeforeTool, "stop", [new("run_exceeds") { Counter = "tool_calls", Limit = 2000 }, new("tool_kind") { ToolKinds = ["read"] }], "When a tool is about to run, if the run has used more than 2,000 tool calls and the tool reads, then stop the run." },
        { RuleWords.BeforeReply, "stop", [], "When the bot is about to reply, stop the run." },
        { RuleWords.BeforeTool, "ask", [new("tool_kind")], "When a tool is about to run, if the tool …, then ask a person first." },
    };

    [Theory]
    [MemberData(nameof(Sentences))]
    public void A_rule_reads_back_as_one_sentence(string checkpoint, string effect, RuleConditionBody[] conditions, string expected) =>
        RuleWords.Sentence(checkpoint, effect, conditions).Should().Be(expected);

    [Fact]
    public void The_closed_section_says_how_many_rules_there_are_and_how_many_are_off()
    {
        var on = new BotRuleResponse(Guid.CreateVersion7(), Guid.CreateVersion7(), "Sample", false, "Sample rule", true, 0, RuleWords.BeforeReply, "stop", []);

        RuleWords.Summary(null).Should().Be("Loading");
        RuleWords.Summary([]).Should().Be("No rules");
        RuleWords.Summary([on]).Should().Be("1 rule");
        RuleWords.Summary([on, on, on with { Enabled = false }]).Should().Be("3 rules, 1 off");
    }

    [Fact]
    public void A_trial_is_worded_from_what_the_server_found()
    {
        RuleWords.Trial(new TryRulesResponse(null, null, null, null, null, null, [])).Should().Be("No rule applies to this sample.");
        RuleWords.Trial(new TryRulesResponse("block", Guid.CreateVersion7(), "Sample rule", "the tool writes", null, null, ["Sample rule"]))
            .Should().Be("The rule \"Sample rule\" would block this: the tool writes.");
    }

    #endregion

    #region The Builder

    [Fact]
    public void Each_moment_offers_only_the_effects_and_conditions_that_mean_something_there()
    {
        RuleWords.Effects(RuleWords.BeforeTool).Select(choice => choice.Value).Should().Equal("ask", "block", "redact", "stop");
        RuleWords.Effects(RuleWords.AfterTool).Select(choice => choice.Value).Should().Equal("block", "redact", "stop");
        RuleWords.Effects(RuleWords.MessageArrives).Select(choice => choice.Value).Should().Equal("block", "stop");
        RuleWords.Conditions(RuleWords.BeforeTool).Select(choice => choice.Value).Should().Contain(["tool_is", "tool_kind", "not_in_list"]);
        RuleWords.Conditions(RuleWords.AfterTool).Select(choice => choice.Value).Should().Contain("tool_is").And.NotContain("not_in_list");
        RuleWords.Conditions(RuleWords.BeforeReply).Select(choice => choice.Value).Should().NotContain(["tool_is", "tool_kind", "not_in_list"]);
    }

    [Fact]
    public void Moving_a_draft_to_another_moment_drops_what_means_nothing_there()
    {
        var draft = new RuleDraft { Checkpoint = RuleWords.BeforeTool, Effect = "ask" };
        draft.Conditions.Add(new ConditionDraft { Kind = "tool_kind" });
        draft.Conditions.Add(new ConditionDraft { Kind = "text_contains", Entries = "one" });

        draft.MoveTo(RuleWords.MessageArrives);

        draft.Conditions.Select(condition => condition.Kind).Should().Equal("text_contains");
        draft.Effect.Should().Be("block", "a message cannot wait for a person");
    }

    [Fact]
    public void A_condition_is_sent_with_only_what_its_kind_uses()
    {
        var draft = new ConditionDraft { Kind = "text_contains", Entries = " one \n\n two ", Pattern = "left over", Argument = "left over" };

        var body = draft.ToBody();

        body.Entries.Should().Equal("one", "two");
        body.Pattern.Should().BeNull();
        body.Argument.Should().BeNull();
        new ConditionDraft { Kind = "time_is", From = "22:00:00", To = "07:00", TimeZone = " UTC " }.ToBody()
            .Should().BeEquivalentTo(new RuleConditionBody("time_is") { From = "22:00", To = "07:00", TimeZone = "UTC" });
        new ConditionDraft { Kind = "run_exceeds", Limit = "many" }.ToBody().Limit.Should().BeNull("the server says what is wrong with it");
    }

    [Fact]
    public void A_saved_rule_opens_in_the_builder_as_it_was_saved()
    {
        var saved = new BotRuleResponse(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Sample",
            false,
            "Sample rule",
            false,
            0,
            RuleWords.BeforeTool,
            "block",
            [new("tool_kind") { ToolKinds = ["write"] }, new("not_in_list") { Argument = "to", Entries = ["example.org"] }]);

        var body = RuleDraft.Of(saved, "UTC").ToBody();

        body.Should().BeEquivalentTo(new BotRuleBody("Sample rule", false, RuleWords.BeforeTool, "block", saved.Conditions));
    }

    [Fact]
    public void Every_starter_is_a_rule_the_builder_could_have_made()
    {
        foreach (var rule in RuleStarters.For("UTC").SelectMany(starter => starter.Rules))
        {
            RuleWords.Effects(rule.Checkpoint!).Select(choice => choice.Value).Should().Contain(rule.Effect);
            rule.Conditions!.Select(condition => condition.Kind).Should().BeSubsetOf(RuleWords.Conditions(rule.Checkpoint!).Select(choice => choice.Value));
        }
    }

    #endregion
}
