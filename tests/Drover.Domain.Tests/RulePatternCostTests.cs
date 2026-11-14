namespace Drover.Domain.Tests;

/// <summary>
/// What a written pattern may cost. The expression engine does not guard against a costly pattern and nothing can
/// stop one match once it has begun, so a server runs written patterns only where its deployment rule says so, and
/// no preset does. Where they are off, one is never built. Where they are on, what can be read off a pattern is
/// refused at save, and a match that fails part way takes the rule's effect.
/// </summary>
public sealed class RulePatternCostTests
{
    #region Private Fields

    // Measured on main at 1b6a587: 2,000 random characters of '4' and 'a' took 17 seconds and 1.8 GB.
    private const string Measured = @"[\s\S]*4[\s\S]{300}Q|Z0";

    // The same shape with its count under the most a pattern may repeat. Nothing that can be read off it refuses
    // it, which is why written patterns are off: it is never run in these tests.
    private const string UnderTheCount = @"[\s\S]*4[\s\S]{250}Q|Z0";

    private static readonly DateTimeOffset now = new(2026, 11, 10, 23, 30, 0, TimeSpan.Zero);

    private static readonly TimeSpan bound = TimeSpan.FromSeconds(1);

    private const long MostAllocated = 64_000_000;

    #endregion

    #region At Save

    [Fact]
    public void The_measured_pattern_is_refused_at_save() =>
        RuleValidator.FindProblem("Sample", RuleCheckpoint.BeforeReply, RuleEffect.Block, [Matches(Measured)]).Should().Contain("more than 256 times");

    [Theory]
    [InlineData(@"a{257}")]
    [InlineData(@"\d{1,300}")]
    [InlineData(@"x{300,}")]
    public void A_count_over_the_most_a_pattern_may_repeat_is_refused_in_words(string pattern) =>
        RuleValidator.FindProblem("Sample", RuleCheckpoint.BeforeReply, RuleEffect.Block, [Matches(pattern)]).Should().Contain("more than 256 times");

    [Theory]
    [InlineData(@"\d{16}")]
    [InlineData(@"[{300}]")]
    [InlineData(@"a\{300\}")]
    [InlineData(@"\b[0-9a-f]{64}\b")]
    [InlineData(@"(?i)password\s*[:=]\s*\S+")]
    [InlineData(@"(?i)\b(confidential|internal only|do not share)\b")]
    [InlineData(@"(?i)secret.{0,40}=")]
    [InlineData(@"https?://[^\s]+")]
    public void A_pattern_a_person_would_write_is_accepted(string pattern) =>
        RuleValidator.FindProblem("Sample", RuleCheckpoint.BeforeReply, RuleEffect.Block, [Matches(pattern)]).Should().BeNull();

    [Fact]
    public void A_pattern_too_large_to_build_is_refused_as_too_large_and_not_as_looking_ahead()
    {
        var problem = RuleValidator.FindProblem("Sample", RuleCheckpoint.BeforeReply, RuleEffect.Block, [Matches(@"(.{200}){200}")]);

        problem.Should().NotBeNull();
        problem.Should().NotContain("looks ahead");
    }

    #endregion

    #region In A Run

    [Fact]
    public void The_measured_pattern_stored_before_it_was_refused_is_not_run_and_takes_the_rules_effect()
    {
        var rule = new ActiveRule(Guid.CreateVersion7(), "Sample rule", RuleCheckpoint.BeforeReply, RuleEffect.Block, [Matches(Measured)]);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate([rule], new RuleSubject { Checkpoint = RuleCheckpoint.BeforeReply, Text = Random("4a", 2_000, 7), Now = now });

        watch.Elapsed.Should().BeLessThan(bound, "this took 17 seconds");
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(MostAllocated, "this held 1.8 GB");
        outcome.Should().NotBeNull("a pattern that cannot be run is not let through: the call passed before");
    }

    [Fact]
    public void A_narrow_gap_stays_within_its_bound_on_long_random_text()
    {
        // A gap of ten after an unanchored start has about a thousand states and no more.
        var accepted = @"[\s\S]*4[\s\S]{10}Q";
        RuleValidator.FindProblem("Sample", RuleCheckpoint.BeforeReply, RuleEffect.Block, [Matches(accepted)]).Should().BeNull();
        var rule = new ActiveRule(Guid.CreateVersion7(), "Sample rule", RuleCheckpoint.BeforeReply, RuleEffect.Block, [Matches(accepted)]);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate([rule], new RuleSubject { Checkpoint = RuleCheckpoint.BeforeReply, Text = Random("4a", RuleBudget.MaxCharacters - 16, 9), Now = now });

        outcome.Should().BeNull("there is no Q in the text");
        watch.Elapsed.Should().BeLessThan(bound);
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(MostAllocated);
    }

    #endregion

    #region A Match That Fails

    [Fact]
    public void A_match_that_runs_out_of_time_spends_the_budget_so_what_follows_cannot_differ()
    {
        var slow = new Regex(@"(\+?){250}\+{250}", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, TimeSpan.FromTicks(1));
        var budget = new RuleBudget(RuleBudget.MaxWork);

        RulePatterns.Find(slow, new string('+', 20_000), budget).Should().BeNull();

        budget.IsSpent.Should().BeTrue("a search that failed is the end of what this evaluation can check");
    }

    [Fact]
    public void A_rule_that_removes_text_never_lets_the_text_through_after_a_match_that_failed()
    {
        // Measured: this pattern on twenty thousand plus signs runs into the match timeout, and the reply went out whole.
        var rule = new ActiveRule(Guid.CreateVersion7(), "Sample rule", RuleCheckpoint.BeforeReply, RuleEffect.Redact, [Matches(@"(\+?){250}\+{250}")]);
        var text = new string('+', 20_000);

        var outcome = RuleEvaluator.Evaluate([rule], new RuleSubject { Checkpoint = RuleCheckpoint.BeforeReply, Text = text, Now = now });

        outcome.Should().NotBeNull();
        outcome!.RedactedText.Should().NotBeNull().And.NotContain("+++", "whether the match finished or failed, what matched is not in what is let through");

        if (outcome.RedactedText == RuleEvaluator.Redaction)
        {
            outcome.Effect.Should().Be(RuleEffect.Block, "a rule that could not search cannot say what is left");
        }
    }

    #endregion

    #region Turned Off

    [Theory]
    [InlineData(RuleEffect.Block)]
    [InlineData(RuleEffect.Redact)]
    public void Where_written_patterns_are_off_a_rule_with_one_is_not_run_and_takes_its_effect(RuleEffect effect)
    {
        var rule = new ActiveRule(Guid.CreateVersion7(), "Sample rule", RuleCheckpoint.BeforeReply, effect, [Matches(UnderTheCount)]) { WrittenPatternsOff = true };
        var budget = new RuleBudget(RuleBudget.MaxWork);

        var outcome = RuleEvaluator.Evaluate([rule], new RuleSubject { Checkpoint = RuleCheckpoint.BeforeReply, Text = "Nothing in particular.", Now = now }, budget)!;

        outcome.Effect.Should().Be(RuleEffect.Block, "a rule that removes text cannot say what is left of a text it did not search");
        outcome.Why.Should().Contain("written patterns are turned off on this server");
        outcome.RedactedText.Should().Be(effect is RuleEffect.Redact ? RuleEvaluator.Redaction : null);
        budget.Spent.Should().Be(0, "the pattern was not run");
        RulePatterns.IsKept(UnderTheCount).Should().BeFalse("and it was not built: no expression exists for it on this server");
    }

    [Fact]
    public void Written_patterns_are_off_in_every_preset_and_no_operator_may_turn_them_on()
    {
        DeploymentPolicy.Hosted.AllowWrittenRulePatterns.Should().BeFalse();
        DeploymentPolicy.SelfHosted.AllowWrittenRulePatterns.Should().BeFalse();
        (DeploymentPolicy.SelfHosted with { AllowWrittenRulePatterns = true }).FindLoosenedRules(DeploymentPolicy.SelfHosted)
            .Should().Contain(nameof(DeploymentPolicy.AllowWrittenRulePatterns), "a server set that way does not start");
    }

    #endregion

    #region What Is Kept

    [Fact]
    public void The_patterns_kept_ready_are_bounded_in_number_and_a_refused_one_keeps_no_expression()
    {
        for (var number = 0; number < RulePatterns.MaxKept + 100; number++)
        {
            RulePatterns.Compile($"word{number}\\d+").Problem.Should().BeNull();
        }

        RulePatterns.Compile(Measured).Expression.Should().BeNull();
        RulePatterns.Compile(Measured).Problem.Should().Contain("more than 256 times");
        RulePatterns.Kept.Should().BeLessThanOrEqualTo(RulePatterns.MaxKept);
    }

    #endregion

    #region Helpers

    private static RuleCondition Matches(string pattern) => new() { Kind = RuleConditionKind.TextMatches, Pattern = pattern };

    private static string Random(string alphabet, int length, int seed)
    {
        var random = new Random(seed);
        return string.Create(length, random, (span, source) =>
        {
            for (var index = 0; index < span.Length; index++)
            {
                span[index] = alphabet[source.Next(alphabet.Length)];
            }
        });
    }

    #endregion
}
