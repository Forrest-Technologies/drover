namespace Drover.Presentation.Tests.Wording;

/// <summary>What a bot has for a person, in words: moved here with the starter for #148, its sentences unchanged.</summary>
public sealed class AttentionWordsTests
{
    #region Attention

    [Theory]
    [InlineData(true, true, true, Attention.WaitingForYou)]
    [InlineData(false, true, true, Attention.Failed)]
    [InlineData(false, false, true, Attention.New)]
    [InlineData(false, false, false, Attention.None)]
    public void Only_the_most_pressing_thing_a_bot_has_is_shown(bool waiting, bool failed, bool hasNew, Attention shown)
    {
        AttentionWords.Of(waiting, failed, hasNew).Should().Be(shown);
    }

    [Fact]
    public void Attention_is_a_word_and_the_tab_counts_who_waits()
    {
        AttentionWords.Word(Attention.None).Should().BeNull();
        AttentionWords.Announce("Sample bot", Attention.WaitingForYou).Should().Be("Sample bot, waiting for you");
        AttentionWords.Announce("Sample bot", Attention.None).Should().Be("Sample bot");
        AttentionWords.TabTitle("Drover", 0).Should().Be("Drover");
        AttentionWords.TabTitle("Drover", 2).Should().Be("(2) Drover");
    }

    [Fact]
    public void Every_failure_cause_names_the_bot_and_says_what_to_do()
    {
        foreach (var cause in Enum.GetValues<FailureCause>())
        {
            var sentence = AttentionWords.Failure(cause, "Sample bot");

            sentence.Should().Contain("Sample bot").And.EndWith(".").And.NotContain("!");
            sentence.Split(". ", StringSplitOptions.RemoveEmptyEntries).Should().HaveCountGreaterThan(1, "it says what failed and what to do");
        }
    }

    [Fact]
    public void A_limit_stop_names_the_limit_with_used_against_cap_and_falls_back_without_limit_data()
    {
        var steps = new RunFailure(Guid.NewGuid(), FailureCauses.BudgetReached, "step_limit: ...", new RunFailureLimit(FailureLimits.ModelCalls, 50, 50));
        var oneStep = new RunFailure(Guid.NewGuid(), FailureCauses.BudgetReached, "step_limit: ...", new RunFailureLimit(FailureLimits.ModelCalls, 1, 1));
        var tokens = new RunFailure(Guid.NewGuid(), FailureCauses.BudgetReached, "token_limit: ...", new RunFailureLimit(FailureLimits.Tokens, 5_012_340, 5_000_000));
        var older = new RunFailure(Guid.NewGuid(), FailureCauses.BudgetReached, "step_limit: ...");

        AttentionWords.Failure(steps, "Slueth").Should().Be("Slueth stopped after 50 of 50 model calls.");
        AttentionWords.Failure(oneStep, "Slueth").Should().Be("Slueth stopped after 1 of 1 model call.");
        AttentionWords.Failure(tokens, "Slueth").Should().Be("Slueth stopped after 5,012,340 of 5,000,000 tokens.");
        AttentionWords.Failure(older, "Slueth").Should().Be("Slueth stopped at a limit on how much one piece of work may use. An owner can change it in the bot's settings, under Limits.");
    }

    [Fact]
    public void The_unmetered_call_stop_names_the_calls_that_reported_no_token_use()
    {
        var unmetered = new RunFailure(Guid.NewGuid(), FailureCauses.BudgetReached, "unmetered_call_limit: ...", new RunFailureLimit(FailureLimits.UnmeteredModelCalls, 200, 200));

        AttentionWords.Failure(unmetered, "Slueth").Should().Be("Slueth stopped after 200 of 200 model calls that reported no token use.");
    }

    [Theory]
    [InlineData(FailureCauses.ModelUnreachable, FailureCause.ModelUnreachable)]
    [InlineData(FailureCauses.ModelRefused, FailureCause.ModelRefused)]
    [InlineData(FailureCauses.ModelBusy, FailureCause.ModelBusy)]
    [InlineData(FailureCauses.ModelOutOfCredit, FailureCause.ModelOutOfCredit)]
    [InlineData(FailureCauses.BudgetReached, FailureCause.BudgetReached)]
    [InlineData(FailureCauses.ServerRestarted, FailureCause.ServerRestarted)]
    [InlineData(FailureCauses.StoppedByRule, FailureCause.StoppedByRule)]
    [InlineData(FailureCauses.Unknown, FailureCause.Unknown)]
    [InlineData("a cause this screen does not know yet", FailureCause.Unknown)]
    public void The_servers_cause_is_read_and_one_it_does_not_know_is_never_guessed_at(string sent, FailureCause read)
    {
        AttentionWords.CauseOf(sent).Should().Be(read);
    }

    #endregion

    #region The Crew

    [Fact]
    public void A_bot_has_the_most_pressing_thing_across_all_its_threads_and_the_tab_counts_bots_not_threads()
    {
        var lead = Guid.NewGuid();
        var helper = Guid.NewGuid();
        ThreadSummary[] threads =
        [
            Thread(lead, new ThreadAttention(false, false, true, null)),
            Thread(lead, new ThreadAttention(true, false, false, null)),
            Thread(lead, new ThreadAttention(true, false, false, null)),
            Thread(helper, new ThreadAttention(false, true, true, null)),
            Thread(helper, null),
        ];

        var crew = AttentionWords.OfCrew(threads);

        crew.Should().BeEquivalentTo(new Dictionary<Guid, Attention> { [lead] = Attention.WaitingForYou, [helper] = Attention.Failed });
        AttentionWords.Waiting(crew).Should().Be(1);
    }

    #endregion

    #region Private Methods

    private static ThreadSummary Thread(Guid botId, ThreadAttention? attention) =>
        new(Guid.NewGuid(), botId, null, false, DateTimeOffset.UnixEpoch, null, false, attention);

    #endregion
}
