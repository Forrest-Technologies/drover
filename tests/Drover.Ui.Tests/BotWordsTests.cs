namespace Drover.Ui.Tests;

/// <summary>The pure parts of the bot editor: schedules in words, and the draft a person types into.</summary>
public sealed class BotWordsTests
{
    #region Public Methods

    [Theory]
    [InlineData(ScheduleRhythm.Daily, "09:30", 1, "30 9 * * *", "Every day at 09:30")]
    [InlineData(ScheduleRhythm.Weekdays, "18:05", 1, "5 18 * * 1-5", "Every weekday at 18:05")]
    [InlineData(ScheduleRhythm.Weekly, "00:00:00", 0, "0 0 * * 0", "Every Sunday at 00:00")]
    public void A_rhythm_and_a_time_become_a_cron_expression_that_reads_back_as_the_same_sentence(
        ScheduleRhythm rhythm, string time, int day, string cron, string sentence)
    {
        ScheduleWords.Cron(rhythm, time, day).Should().Be(cron);
        ScheduleWords.Describe(cron).Should().Be(sentence);
        ScheduleWords.Read(cron).Rhythm.Should().Be(rhythm);
    }

    [Theory]
    [InlineData("*/5 * * * *")]
    [InlineData("0 9 1 * *")]
    [InlineData("not a schedule")]
    public void Any_other_expression_is_shown_as_it_is(string cron)
    {
        ScheduleWords.Read(cron).Rhythm.Should().Be(ScheduleRhythm.Custom);
        ScheduleWords.Describe(cron).Should().Be(cron);
    }

    [Theory]
    [InlineData("")]
    [InlineData("25:00")]
    [InlineData("soon")]
    public void A_time_that_does_not_read_gives_no_expression(string time) =>
        ScheduleWords.Cron(ScheduleRhythm.Daily, time, 1).Should().BeNull();

    [Fact]
    public void A_draft_differs_only_when_it_would_save_something_else()
    {
        var saved = new BotDraft { Name = "Atlas", Handle = "atlas", Model = "sample" };
        var same = new BotDraft { Name = "Atlas", Handle = "atlas ", Model = "sample", MaxChildren = " " };
        var changed = new BotDraft { Name = "Atlas", Handle = "atlas", Model = "sample", AllowedHosts = "example.com\nexample.org" };

        same.DiffersFrom(saved).Should().BeFalse();
        changed.DiffersFrom(saved).Should().BeTrue();
        changed.ToRequest().SandboxSettings!.AllowedHosts.Should().Equal("example.com", "example.org");
    }

    [Fact]
    public void Settings_the_chosen_model_lacks_are_cleared_and_an_unknown_model_keeps_them()
    {
        var draft = new BotDraft { ReasoningEffort = "high", WebSearch = "on", StructuredOutput = true };

        draft.KeepWhatModelSupports(null);
        var kept = (draft.ReasoningEffort, draft.WebSearch, draft.StructuredOutput);
        draft.KeepWhatModelSupports(new ModelCapabilitiesResponse(null, null, true, true, false, false, [], false, false));

        kept.Should().Be(("high", "on", true));
        (draft.ReasoningEffort, draft.WebSearch, draft.StructuredOutput).Should().Be((string.Empty, "off", false));
    }

    #endregion
}
