namespace Drover.Ui.Tests.Bots;

/// <summary>
/// Routines in words: building a schedule from plain choices and reading it back, the sentence a routine is listed
/// and read out as, and how its last run went. The sentences here are the design the routine starter settled.
/// </summary>
public sealed class RoutineWordsTests
{
    #region Private Fields

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region Building

    public static TheoryData<RoutineWhen, string, string, string> Choices => new()
    {
        { RoutineWhen.Start with { Rhythm = RoutineRhythm.EveryDay, Time = new(8, 0) }, "Europe/Berlin", "0 8 * * *", "Every day at 08:00, Berlin time" },
        { RoutineWhen.Start with { Rhythm = RoutineRhythm.Weekdays, Time = new(7, 30) }, "America/New_York", "30 7 * * 1-5", "Every weekday at 07:30, New York time" },
        {
            RoutineWhen.Start with { Rhythm = RoutineRhythm.ChosenDays, Time = new(9, 0), Days = [DayOfWeek.Friday, DayOfWeek.Monday, DayOfWeek.Wednesday] },
            "UTC", "0 9 * * 1,3,5", "Every Monday, Wednesday and Friday at 09:00, UTC"
        },
        { RoutineWhen.Start with { Rhythm = RoutineRhythm.ChosenDays, Time = new(9, 0), Days = [DayOfWeek.Sunday] }, "", "0 9 * * 0", "Every Sunday at 09:00, UTC" },
        { RoutineWhen.Start with { Rhythm = RoutineRhythm.DayOfMonth, Time = new(6, 15), DayOfMonth = 1 }, "Asia/Tokyo", "15 6 1 * *", "On the 1st of every month at 06:15, Tokyo time" },
        {
            RoutineWhen.Start with { Rhythm = RoutineRhythm.EveryHours, Time = new(0, 20), Hours = 4 },
            "America/Argentina/Buenos_Aires", "20 */4 * * *", "Every 4 hours, at 20 past, Buenos Aires time"
        },
        { RoutineWhen.Start with { Rhythm = RoutineRhythm.EveryHours, Time = new(0, 0), Hours = 1 }, "Etc/UTC", "0 */1 * * *", "Every hour, on the hour, UTC" },
        { RoutineWhen.Start with { Rhythm = RoutineRhythm.Custom, Cron = " */20 9-17 * * 1-5 " }, "Europe/Berlin", "*/20 9-17 * * 1-5", "On a custom schedule, Berlin time" },
    };

    [Theory]
    [MemberData(nameof(Choices))]
    public void Each_choice_builds_its_cron_expression_and_reads_back_as_its_sentence_in_its_time_zone(
        RoutineWhen when, string timeZone, string cron, string sentence)
    {
        RoutineWords.Cron(when).Should().Be(cron);
        RoutineWords.Sentence(cron, timeZone).Should().Be(sentence);
        RoutineWords.Read(cron).Rhythm.Should().Be(when.Rhythm);
    }

    #endregion

    #region Sentences

    [Theory]
    [InlineData("0 8 * * *", "Every day at 08:00")]
    [InlineData("30 7 * * 1-5", "Every weekday at 07:30")]
    [InlineData("0 9 * * 1,3,5", "Every Monday, Wednesday and Friday at 09:00")]
    [InlineData("0 9 * * 0", "Every Sunday at 09:00")]
    [InlineData("0 9 * * 7", "Every Sunday at 09:00")]
    [InlineData("15 6 1 * *", "On the 1st of every month at 06:15")]
    [InlineData("0 6 22 * *", "On the 22nd of every month at 06:00")]
    [InlineData("0 */4 * * *", "Every 4 hours, on the hour")]
    [InlineData("0 */12 * * *", "Every 12 hours, on the hour")]
    [InlineData("20 */1 * * *", "Every hour, at 20 past")]
    public void A_schedule_the_editor_builds_reads_as_a_sentence(string cron, string sentence)
    {
        RoutineWords.Describe(cron).Should().Be(sentence);
    }

    [Theory]
    [InlineData("*/20 9-17 * * 1-5")]
    [InlineData("0 8 * 6 *")]
    [InlineData("0 8 1 * 1")]
    [InlineData("61 8 * * *")]
    [InlineData("0 */5 * * *")]
    [InlineData("0 */13 * * *")]
    [InlineData("0 9 31 * *")]
    [InlineData("0 9 29 * *")]
    [InlineData("not a schedule")]
    public void A_schedule_the_editor_could_not_build_has_no_sentence_and_reads_as_custom(string cron)
    {
        RoutineWords.Describe(cron).Should().BeNull();
        RoutineWords.Read(cron).Rhythm.Should().Be(RoutineRhythm.Custom);
        RoutineWords.Sentence(cron, "Europe/Berlin").Should().Be("On a custom schedule, Berlin time");
    }

    [Theory]
    [InlineData("0 8 * * *")]
    [InlineData("30 7 * * 1-5")]
    [InlineData("0 9 * * 1,3,5")]
    [InlineData("15 6 1 * *")]
    [InlineData("5 */4 * * *")]
    public void Reading_a_schedule_and_building_it_again_gives_the_same_schedule(string cron)
    {
        RoutineWords.Cron(RoutineWords.Read(cron)).Should().Be(cron);
    }

    [Fact]
    public void Fields_that_do_not_make_a_schedule_build_nothing()
    {
        RoutineWords.Cron(RoutineWhen.Start with { Rhythm = RoutineRhythm.ChosenDays, Days = [] }).Should().BeNull();
        RoutineWords.Cron(RoutineWhen.Start with { Rhythm = RoutineRhythm.DayOfMonth, DayOfMonth = 29 }).Should().BeNull("not every month has a 29th");
        RoutineWords.Cron(RoutineWhen.Start with { Rhythm = RoutineRhythm.EveryHours, Hours = 0 }).Should().BeNull();
        RoutineWords.Cron(RoutineWhen.Start with { Rhythm = RoutineRhythm.EveryHours, Hours = 5 }).Should().BeNull("every 5 hours from midnight leaves a 4 hour gap at the end of the day");
        RoutineWords.Cron(RoutineWhen.Start with { Rhythm = RoutineRhythm.Custom, Cron = " " }).Should().BeNull();
    }

    [Theory]
    [InlineData("Europe/Berlin", "Berlin time")]
    [InlineData("America/New_York", "New York time")]
    [InlineData("America/Argentina/Buenos_Aires", "Buenos Aires time")]
    [InlineData("UTC", "UTC")]
    [InlineData("", "UTC")]
    public void A_time_zone_is_said_the_way_a_person_says_it(string zone, string words)
    {
        RoutineWords.Zone(zone).Should().Be(words);
    }

    [Fact]
    public void A_routines_last_run_is_said_in_words()
    {
        RoutineWords.Outcome(null, string.Empty).Should().Be("Has not run yet");
        RoutineWords.Outcome(new(start, RoutineOutcome.Finished, null, null), "today at 08:00").Should().Be("Ran today at 08:00 and finished");
        RoutineWords.Outcome(new(start, RoutineOutcome.Failed, "the model could not be reached", null), "today at 08:00")
            .Should().Be("Failed today at 08:00: the model could not be reached");
        RoutineWords.Outcome(new(start, RoutineOutcome.DidNotRun, "Sample bot was paused", null), "today at 08:00")
            .Should().Be("Did not run today at 08:00: Sample bot was paused");
    }

    [Fact]
    public void The_closed_routines_section_says_how_many_and_how_many_are_off()
    {
        RoutineItem Routine(bool enabled) => new(Guid.NewGuid(), "Sample", "0 8 * * *", "UTC", enabled, null, null);

        RoutineWords.Summary([]).Should().Be("No routines");
        RoutineWords.Summary([Routine(true)]).Should().Be("1 routine");
        RoutineWords.Summary([Routine(true), Routine(false), Routine(true)]).Should().Be("3 routines, 1 off");
    }

    [Fact]
    public void A_proposed_routine_shows_the_whole_prompt_and_a_change_shows_before_and_after()
    {
        var prompt = "Line one.\nLine two.";

        RoutineWords.Proposal("0 8 * * 1-5", "Europe/Berlin", prompt).Should().Equal(
            new ToolFact("When", "Every weekday at 08:00, Berlin time"),
            new ToolFact("What it is sent", prompt));

        var before = new RoutineItem(Guid.NewGuid(), "Old prompt.", "0 9 * * *", "UTC", true, null, null);
        RoutineWords.Proposal("0 8 * * 1-5", "Europe/Berlin", prompt, before).Select(fact => fact.Name).Should().Equal(
            "When, now", "What it is sent, now", "When, after the change", "What it is sent, after the change");
    }

    #endregion

    #region Last Run

    [Theory]
    [InlineData("completed", null, true, "Ran Mon 5 Jan, 08:00 and finished")]
    [InlineData("failed", "The model connection could not be reached.", true, "Failed Mon 5 Jan, 08:00: The model connection could not be reached.")]
    [InlineData("cancelled", null, true, "Started Mon 5 Jan, 08:00 and was stopped before it finished")]
    [InlineData("running", null, true, "Started Mon 5 Jan, 08:00 and is still under way")]
    [InlineData("waiting", null, true, "Started Mon 5 Jan, 08:00 and waits for an answer in its conversation")]
    [InlineData("waiting_for_reply", null, false, "Sent Mon 5 Jan, 08:00, and waits for the reply under way in its conversation")]
    [InlineData("started", null, false, "Started Mon 5 Jan, 08:00, in the private conversation of the person who made it")]
    [InlineData("bot_paused", null, false, "Did not run Mon 5 Jan, 08:00: Sample bot was paused")]
    [InlineData("owner_left", null, false, "Did not run Mon 5 Jan, 08:00: the person who made it left the space, so it was turned off")]
    public void A_last_run_from_the_server_is_said_in_words_and_links_only_to_a_conversation_the_server_gave(
        string outcome, string? reason, bool linked, string words)
    {
        var threadId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000b1");
        var schedule = new BotScheduleResponse(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "0 8 * * *", "UTC", "Sample", "new_thread", true, null,
            linked ? Guid.NewGuid() : null, start, outcome, linked ? threadId : null, reason);

        var last = RoutineWords.Item(schedule, "Sample bot").LastRun!;

        RoutineWords.Outcome(last, "Mon 5 Jan, 08:00").Should().Be(words);
        last.ConversationHref.Should().Be(linked ? $"/threads/{threadId}" : null);
    }

    [Theory]
    [InlineData("Europe/Berlin", "Mon 5 Jan, 09:00")]
    [InlineData("America/New_York", "Mon 5 Jan, 03:00")]
    [InlineData("Asia/Tokyo", "Mon 5 Jan, 17:00")]
    [InlineData("Not/AZone", "Mon 5 Jan, 08:00 UTC")]
    public void A_routines_times_are_said_in_its_own_time_zone_so_they_agree_with_its_sentence(string zone, string words)
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);

        RoutineWords.Moment(start, zone).Should().Be(words);
    }

    [Fact]
    public void A_routine_that_has_not_been_due_has_no_last_run()
    {
        var schedule = new BotScheduleResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "0 8 * * *", "UTC", "Sample", "new_thread", true, start);

        RoutineWords.Item(schedule, "Sample bot").LastRun.Should().BeNull();
    }

    #endregion

    #region Nested Types

    /// <summary>Sets the current culture for one test and puts the old one back.</summary>
    private sealed class CultureScope : IDisposable
    {
        #region Private Fields

        private readonly CultureInfo before = CultureInfo.CurrentCulture;

        #endregion

        #region Constructors

        public CultureScope(CultureInfo culture) => CultureInfo.CurrentCulture = culture;

        #endregion

        #region Interface Implementations

        public void Dispose() => CultureInfo.CurrentCulture = before;

        #endregion
    }

    #endregion
}
