namespace Drover.Ui.Tests.Chat;

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

    [Theory]
    [InlineData(FailureCauses.ModelUnreachable, FailureCause.ModelUnreachable)]
    [InlineData(FailureCauses.ModelRefused, FailureCause.ModelRefused)]
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
