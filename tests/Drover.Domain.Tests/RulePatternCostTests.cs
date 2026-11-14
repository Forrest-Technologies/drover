namespace Drover.Domain.Tests;

/// <summary>
/// What a written pattern may cost. The expression engine does not guard against a costly pattern and its match
/// timeout does not fire while it builds its states, so a pattern is tried on sample text when it is saved and when
/// it is first read, and one that costs too much never touches real text.
/// </summary>
public sealed class RulePatternCostTests
{
    #region Private Fields

    // Measured on main at 1b6a587: 2,000 random characters of '4' and 'a' took 17 seconds and 1.8 GB.
    private const string Measured = @"[\s\S]*4[\s\S]{300}Q|Z0";

    // The same shape with its count under the most a pattern may repeat, so only trying it finds what it costs.
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
    [InlineData(UnderTheCount)]
    [InlineData(@"4.{0,200}Q")]
    [InlineData(@"[\s\S]*4[\s\S]{20}Q")]
    [InlineData(@"(a|b)*a(a|b){12}")]
    [InlineData(@"(4|a)*4(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)Q")]
    public void A_pattern_that_costs_too_much_on_sample_text_is_refused_at_save_in_words(string pattern)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();

        var problem = RuleValidator.FindProblem("Sample", RuleCheckpoint.BeforeReply, RuleEffect.Block, [Matches(pattern)]);

        problem.Should().Contain("too much work");
        watch.Elapsed.Should().BeLessThan(bound, "trying a pattern is itself bounded");
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(MostAllocated);
    }

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

    [Theory]
    [InlineData(Measured, 2_000)]
    [InlineData(Measured, 499_000)]
    [InlineData(UnderTheCount, 2_000)]
    [InlineData(UnderTheCount, 499_000)]
    [InlineData(@"4.{0,200}Q", 499_000)]
    [InlineData(@"[\s\S]*4[\s\S]{20}Q", 499_000)]
    [InlineData(@"(a|b)*a(a|b){12}", 499_000)]
    [InlineData(@"(4|a)*4(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)(4|a)Q", 499_000)]
    public void A_stored_pattern_that_costs_too_much_is_not_run_and_takes_the_rules_effect(string pattern, int length)
    {
        // The validator refuses it. A rule saved before it did is read the same way and never reaches real text.
        var rule = new ActiveRule(Guid.CreateVersion7(), "Sample rule", RuleCheckpoint.BeforeReply, RuleEffect.Block, [Matches(pattern)]);
        var text = Random(pattern.Contains("(a|b)", StringComparison.Ordinal) ? "ab" : "4a", length, 7);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();

        var outcome = RuleEvaluator.Evaluate([rule], new RuleSubject { Checkpoint = RuleCheckpoint.BeforeReply, Text = text, Now = now });

        watch.Elapsed.Should().BeLessThan(bound, "this took 17 seconds");
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(MostAllocated, "this held 1.8 GB");
        outcome.Should().NotBeNull("a pattern that cannot be run is not let through: the call passed before");
    }

    [Fact]
    public void The_worst_pattern_that_is_accepted_stays_within_its_bound_on_long_random_text()
    {
        // A gap of ten after an unanchored start has about a thousand states and no more, so it passes the trial.
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
    }

    [Fact]
    public void The_hosted_preset_turns_written_patterns_off_and_the_self_hosted_one_leaves_them_on()
    {
        DeploymentPolicy.Hosted.AllowWrittenRulePatterns.Should().BeFalse();
        DeploymentPolicy.SelfHosted.AllowWrittenRulePatterns.Should().BeTrue();
    }

    #endregion

    #region What Is Kept

    [Fact]
    public void The_patterns_kept_ready_are_bounded_in_number_and_in_what_they_hold()
    {
        for (var number = 0; number < RulePatterns.MaxKept + 100; number++)
        {
            RulePatterns.Compile($"word{number}\\d+").Problem.Should().BeNull();
        }

        RulePatterns.Compile(UnderTheCount).Expression.Should().BeNull("a pattern that was refused keeps no expression");
        RulePatterns.Compile(UnderTheCount).Problem.Should().Contain("too much work", "its refusal is kept, so it is not tried again each time it is read");

        var (patterns, bytes) = RulePatterns.Held;
        patterns.Should().BeLessThanOrEqualTo(RulePatterns.MaxKept);
        bytes.Should().BeLessThanOrEqualTo(RulePatterns.MaxKeptBytes);
    }

    [Fact]
    public void A_refused_pattern_is_refused_again_without_being_tried_again()
    {
        RulePatterns.Compile(UnderTheCount);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var again = RulePatterns.Compile(UnderTheCount);

        again.Problem.Should().Contain("too much work");
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(100_000);
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
