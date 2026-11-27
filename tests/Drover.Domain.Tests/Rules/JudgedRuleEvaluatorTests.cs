namespace Drover.Domain.Tests.Rules;

/// <summary>
/// Rules written as a sentence, decided in code from the probabilities a decision model gives. One case for each cell
/// of the table in #1018: what an effect does when the rule applies, when it does not, and when nobody is sure.
/// Judged rules only narrow, and whatever is not sure goes to a person.
/// </summary>
public sealed class JudgedRuleEvaluatorTests
{
    #region Private Fields

    private const string Statement = "Always ask me before sending an email.";

    private static readonly RuleSubject call = new()
    {
        Checkpoint = RuleCheckpoint.BeforeTool,
        ToolName = "email.send",
        ToolKind = ToolSensitivity.Write,
        Json = JsonElement.Parse("""{"to":"sam@example.org"}"""),
    };

    private static readonly RuleSubject reply = new() { Checkpoint = RuleCheckpoint.BeforeReply, Text = "Here is the summary." };

    #endregion

    #region The Table

    [Theory]
    [InlineData(RuleEffect.Ask, 0.92, 0.0, RuleEffect.Ask)]
    [InlineData(RuleEffect.Ask, 0.80, 0.0, RuleEffect.Ask)]
    [InlineData(RuleEffect.Block, 0.92, 0.10, RuleEffect.Block)]
    [InlineData(RuleEffect.Stop, 0.92, 0.0, RuleEffect.Stop)]
    public void At_or_above_the_threshold_the_effect_applies(RuleEffect effect, double applies, double met, RuleEffect expected)
    {
        var rule = Judged(effect);

        Evaluate(rule, call, new JudgedVerdict(applies, met))!.Effect.Should().Be(expected);
    }

    [Fact]
    public void A_block_rule_whose_requirement_was_done_lets_the_call_run()
    {
        var rule = Judged(RuleEffect.Block);

        Evaluate(rule, call, new JudgedVerdict(0.95, 0.90)).Should().BeNull("what the rule requires before the action has been done");
        Evaluate(rule, call, new JudgedVerdict(0.95, 0.79))!.Effect.Should().Be(RuleEffect.Block, "below the threshold, done is not shown");
    }

    [Fact]
    public void A_block_rule_tells_why_without_the_checked_text()
    {
        var outcome = Evaluate(Judged(RuleEffect.Block), call, new JudgedVerdict(0.95, 0.10))!;

        outcome.Why.Should().Contain("95%").And.NotContain("sam@example.org");
    }

    [Theory]
    [InlineData(RuleEffect.Ask)]
    [InlineData(RuleEffect.Block)]
    [InlineData(RuleEffect.Stop)]
    public void At_or_below_the_pass_line_nothing_happens(RuleEffect effect)
    {
        Evaluate(Judged(effect), call, new JudgedVerdict(0.20, 0.0)).Should().BeNull();
        Evaluate(Judged(effect), call, new JudgedVerdict(0.03, 0.0)).Should().BeNull();
    }

    [Theory]
    [InlineData(RuleEffect.Ask)]
    [InlineData(RuleEffect.Block)]
    [InlineData(RuleEffect.Stop)]
    public void Between_the_lines_a_person_is_asked_before_a_tool(RuleEffect effect)
    {
        var outcome = Evaluate(Judged(effect), call, new JudgedVerdict(0.61, 0.99))!;

        outcome.Effect.Should().Be(RuleEffect.Ask);
        outcome.Why.Should().Contain("61%").And.Contain("a person is asked");
    }

    [Theory]
    [InlineData(RuleEffect.Ask)]
    [InlineData(RuleEffect.Block)]
    [InlineData(RuleEffect.Stop)]
    public void A_failed_check_asks_a_person_before_a_tool(RuleEffect effect)
    {
        Evaluate(Judged(effect), call, JudgedVerdict.Failed("it took longer than 5 seconds"))!.Effect.Should().Be(RuleEffect.Ask);
        Evaluate(Judged(effect), call, JudgedVerdict.Unchecked("the run has made every rule check it may"))!.Effect.Should().Be(RuleEffect.Ask);
    }

    [Theory]
    [InlineData(RuleEffect.Block)]
    [InlineData(RuleEffect.Stop)]
    public void Before_a_reply_unsure_holds_the_reply_back(RuleEffect effect)
    {
        var rule = Judged(effect, RuleCheckpoint.BeforeReply);

        Evaluate(rule, reply, new JudgedVerdict(0.5, 0.5))!.Effect.Should().Be(RuleEffect.Block);
        Evaluate(rule, reply, JudgedVerdict.Failed("the answer could not be read"))!.Effect.Should().Be(RuleEffect.Block);
        Evaluate(rule, reply, new JudgedVerdict(0.1, 0.0)).Should().BeNull();
    }

    [Fact]
    public void The_rule_s_own_threshold_decides()
    {
        var rule = Judged(RuleEffect.Ask, confidence: 0.95);

        Evaluate(rule, call, new JudgedVerdict(0.92, 0.0))!.Why.Should().Contain("not sure", "0.92 is below this rule's 0.95");
        Evaluate(rule, call, new JudgedVerdict(0.06, 0.0))!.Effect.Should().Be(RuleEffect.Ask, "0.06 is above this rule's pass line of 0.05");
        Evaluate(rule, call, new JudgedVerdict(0.05, 0.0)).Should().BeNull();
    }

    #endregion

    #region Order Of Deciding

    [Fact]
    public void A_rule_with_no_verdict_is_unsure()
    {
        var rule = Judged(RuleEffect.Stop);

        var outcome = RuleEvaluator.Evaluate([rule], call)!;

        outcome.Effect.Should().Be(RuleEffect.Ask, "no verdict is no answer, and no answer goes to a person");
        outcome.Why.Should().Contain("not checked");
    }

    [Fact]
    public void The_strictest_effect_decides_among_hard_and_judged_rules()
    {
        var hardAsk = Hard(RuleEffect.Ask);
        var judgedStop = Judged(RuleEffect.Stop);

        var outcome = RuleEvaluator.Evaluate([hardAsk, judgedStop], call, Verdicts(judgedStop, new JudgedVerdict(0.97, 0.0)))!;

        outcome.Effect.Should().Be(RuleEffect.Stop);
        outcome.Rule.Should().Be(judgedStop);
        outcome.Applied.Should().Equal(judgedStop, hardAsk);
    }

    [Fact]
    public void A_hard_refusal_is_decided_first_and_nothing_is_judged()
    {
        var hardBlock = Hard(RuleEffect.Block);
        var judgedStop = Judged(RuleEffect.Stop);

        var due = RuleEvaluator.FindJudgmentsDue([judgedStop, hardBlock], call);

        due.Rules.Should().BeEmpty("a call a rule decided by code refuses is never sent to a decision model");
        due.Hard!.Rule.Should().Be(hardBlock);
        RuleEvaluator.Evaluate([judgedStop, hardBlock], call, Verdicts(judgedStop, new JudgedVerdict(0.99, 0.0)))!.Rule.Should().Be(hardBlock);
    }

    [Fact]
    public void Only_judged_rules_whose_other_conditions_hold_are_due()
    {
        var aboutWrites = Judged(RuleEffect.Ask) with { Conditions = [Kind(ToolSensitivity.Write), Sentence()] };
        var aboutReads = Judged(RuleEffect.Ask) with { Id = Guid.CreateVersion7(), Conditions = [Kind(ToolSensitivity.Read), Sentence()] };
        var atReplies = Judged(RuleEffect.Block, RuleCheckpoint.BeforeReply);

        RuleEvaluator.FindJudgmentsDue([aboutWrites, aboutReads, atReplies], call).Rules.Should().Equal(aboutWrites);
    }

    [Fact]
    public void A_rule_the_server_does_not_judge_is_never_due_and_counts_as_unsure()
    {
        var rule = Judged(RuleEffect.Stop) with { JudgedOff = true };

        RuleEvaluator.FindJudgmentsDue([rule], call).Rules.Should().BeEmpty();
        var outcome = RuleEvaluator.Evaluate([rule], call, Verdicts(rule, new JudgedVerdict(0.99, 0.0)))!;
        outcome.Effect.Should().Be(RuleEffect.Ask, "a verdict for a rule that is turned off is not used");
        outcome.Why.Should().Contain("turned off");
    }

    [Fact]
    public void A_redacting_rule_still_rewrites_beside_a_judged_rule()
    {
        var redact = Hard(RuleEffect.Redact) with { Conditions = [new() { Kind = RuleConditionKind.TextHolds, Builtin = BuiltinPattern.EmailAddress }] };
        var judgedAsk = Judged(RuleEffect.Ask);

        var due = RuleEvaluator.FindJudgmentsDue([redact, judgedAsk], call);
        var outcome = RuleEvaluator.Evaluate([redact, judgedAsk], call, Verdicts(judgedAsk, new JudgedVerdict(0.9, 0.0)))!;

        due.Hard!.RedactedJson!.Value.GetRawText().Should().NotContain("sam@example.org", "the action is sent as the rules decided by code left it");
        outcome.Effect.Should().Be(RuleEffect.Ask);
        outcome.RedactedJson!.Value.GetRawText().Should().NotContain("sam@example.org");
    }

    #endregion

    #region Saving

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_judged_rule_that_cannot_hold_is_refused_in_words(string what, RuleCheckpoint checkpoint, RuleEffect effect, RuleCondition[] conditions, string expected)
    {
        RuleValidator.FindProblem("Sample", checkpoint, effect, conditions).Should().Contain(expected, what);
    }

    [Fact]
    public void A_judged_rule_from_the_issue_holds()
    {
        RuleValidator.FindProblem("Ask before email", RuleCheckpoint.BeforeTool, RuleEffect.Ask, [Kind(ToolSensitivity.Write), Sentence()]).Should().BeNull();
        RuleValidator.FindProblem("Verify first", RuleCheckpoint.BeforeTool, RuleEffect.Block, [Sentence("Always verify the invoice total matches the purchase order before paying it.")])
            .Should().BeNull();
        RuleValidator.FindProblem("No promises", RuleCheckpoint.BeforeReply, RuleEffect.Stop, [Sentence() with { MinConfidence = 0.99 }]).Should().BeNull();
    }

    public static TheoryData<string, RuleCheckpoint, RuleEffect, RuleCondition[], string> Refused => new()
    {
        { "two sentences in one rule", RuleCheckpoint.BeforeTool, RuleEffect.Ask, [Sentence(), Sentence("Never pay twice.")], "one sentence" },
        { "after a tool", RuleCheckpoint.AfterTool, RuleEffect.Block, [Sentence()], "before a tool is used or before the bot replies" },
        { "when a message arrives", RuleCheckpoint.MessageArrives, RuleEffect.Block, [Sentence()], "before a tool is used or before the bot replies" },
        { "redacting", RuleCheckpoint.BeforeReply, RuleEffect.Redact, [Sentence()], "cannot redact" },
        { "no sentence", RuleCheckpoint.BeforeTool, RuleEffect.Ask, [Sentence("  ")], "one sentence" },
        { "too long", RuleCheckpoint.BeforeTool, RuleEffect.Ask, [Sentence(new string('a', RuleValidator.MaxValueLength + 1))], "one sentence" },
        { "too unsure", RuleCheckpoint.BeforeTool, RuleEffect.Ask, [Sentence() with { MinConfidence = 0.49 }], "0.50 to 0.99" },
        { "too sure", RuleCheckpoint.BeforeTool, RuleEffect.Ask, [Sentence() with { MinConfidence = 1.0 }], "0.50 to 0.99" },
        { "not a number", RuleCheckpoint.BeforeTool, RuleEffect.Ask, [Sentence() with { MinConfidence = double.NaN }], "0.50 to 0.99" },
    };

    #endregion

    #region Private Methods

    private static RuleOutcome? Evaluate(ActiveRule rule, RuleSubject subject, JudgedVerdict verdict) =>
        RuleEvaluator.Evaluate([rule], subject, Verdicts(rule, verdict));

    private static Dictionary<ActiveRule, JudgedVerdict> Verdicts(ActiveRule rule, JudgedVerdict verdict) => new() { [rule] = verdict };

    private static ActiveRule Judged(RuleEffect effect, RuleCheckpoint checkpoint = RuleCheckpoint.BeforeTool, double? confidence = null) =>
        new(Guid.CreateVersion7(), "Sentence rule", checkpoint, effect, [Sentence() with { MinConfidence = confidence }]);

    private static ActiveRule Hard(RuleEffect effect) =>
        new(Guid.CreateVersion7(), "Hard rule", RuleCheckpoint.BeforeTool, effect, [new() { Kind = RuleConditionKind.ToolIs, Tools = ["email.send"] }]);

    private static RuleCondition Sentence(string statement = Statement) => new() { Kind = RuleConditionKind.Judged, Statement = statement };

    private static RuleCondition Kind(ToolSensitivity kind) => new() { Kind = RuleConditionKind.ToolKind, ToolKinds = [kind] };

    #endregion
}
