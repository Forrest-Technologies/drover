namespace Drover.Ui.Tests.Bots;

/// <summary>
/// A routine's row and the "When" fields as components: what they render, what assistive technology reads, and what
/// they hand back. The first tests came with the routine starter.
/// </summary>
public sealed class RoutineComponentTests : BunitContext
{
    #region Private Fields

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region Constructors

    public RoutineComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
    }

    #endregion

    #region Starter Tests

    [Fact]
    public void A_routine_is_headed_by_its_sentence_and_only_those_who_may_change_it_get_controls()
    {
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.\nKeep it short.", "0 8 * * 1-5", "Europe/Berlin", true, start, null);
        var row = Render<RoutineRow>(set => set.Add(view => view.Routine, routine).Add(view => view.BotName, "Sample bot"));

        row.Find("h3").TextContent.Should().Be("Every weekday at 08:00, Berlin time");
        row.Find(".dv-routine-prompt").TextContent.Should().Be("Write the brief.");
        row.Find("article").GetAttribute("aria-labelledby").Should().Be(row.Find("h3").Id);
        row.FindAll("button").Should().BeEmpty();

        row.Render(set => set.Add(view => view.CanChange, true).Add(view => view.BotPaused, true));

        row.FindAll("button").Select(button => button.TextContent.Trim()).Should().Contain(["Run once now", "Change", "Remove"]);
        row.Find(".dv-routine-facts").TextContent.Should().Contain("Does not run while Sample bot is paused.").And.Contain("Has not run yet");
    }

    [Fact]
    public void Choosing_when_shows_only_that_choices_fields_and_reads_the_schedule_back()
    {
        var when = RoutineWhen.Start;
        var fields = Render<RoutineWhenFields>(set => set
            .Add(view => view.When, when)
            .Add(view => view.TimeZone, "Europe/Berlin")
            .Add(view => view.WhenChanged, value => when = value));

        fields.Find("[role=status]").TextContent.Should().Be("Reads as: Every weekday at 09:00, Berlin time.");
        fields.FindAll("fieldset").Should().BeEmpty();

        fields.Render(set => set.Add(view => view.When, when with { Rhythm = RoutineRhythm.ChosenDays, Days = [] }));

        fields.FindAll("fieldset input[type=checkbox]").Should().HaveCount(7);
        fields.Find("[role=status]").TextContent.Should().Be("Choose at least one day.");

        fields.FindAll("fieldset input[type=checkbox]")[2].Change(true);

        when.Days.Should().Equal(DayOfWeek.Wednesday);
    }

    #endregion

    #region Product

    [Fact]
    public void Only_the_routines_owner_may_change_it_and_every_control_is_named_with_its_sentence()
    {
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.", "0 8 * * 1-5", "Europe/Berlin", false, null, null);
        var row = Render<RoutineRow>(set => set
            .Add(view => view.Routine, routine)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.CanChange, true)
            .Add(view => view.CanRewrite, false));

        row.FindAll("button").Select(button => button.TextContent.Trim()).Should().Equal("Run once now", "Remove");
        row.Find(".dv-routine-note").TextContent.Should().Be("Only the person who made it can change what it sends or when, because it runs as them.");
        row.FindAll("button").Should().OnlyContain(button => button.GetAttribute("aria-describedby") == row.Find("h3").Id);
        row.Find("[role=switch]").Closest("label")!.TextContent.Should().Contain("Runs on its own").And.Contain("Every weekday at 08:00, Berlin time: Write the brief.");
        row.Find(".dv-routine-facts").TextContent.Should().Contain("Off. It does not run until it is turned on.");
    }

    [Fact]
    public void A_routine_run_once_links_to_its_conversation_and_a_refusal_is_said_in_the_row()
    {
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.", "0 8 * * *", "UTC", true, start, null);
        var row = Render<RoutineRow>(set => set
            .Add(view => view.Routine, routine)
            .Add(view => view.BotName, "Sample bot")
            .Add(view => view.StartedHref, "/threads/0196f1a0-0000-7000-8000-0000000000b1")
            .Add(view => view.Problem, "Sample bot is paused, so its routines do not run. Resume it first."));

        row.Find(".dv-routine-started a").GetAttribute("href").Should().Be("/threads/0196f1a0-0000-7000-8000-0000000000b1");
        row.Find(".dv-routine-started a").TextContent.Should().Be("Open the conversation it started");
        row.Markup.Should().Contain("Sample bot is paused, so its routines do not run. Resume it first.");
    }

    [Fact]
    public void A_custom_expression_is_explained_by_its_next_times_or_refused_with_the_servers_sentence()
    {
        var when = RoutineWhen.Start with { Rhythm = RoutineRhythm.Custom, Cron = "*/20 9-17 * * 1-5" };
        var fields = Render<RoutineWhenFields>(set => set.Add(view => view.When, when).Add(view => view.TimeZone, "UTC"));

        fields.Find(".dv-when-reads").TextContent.Should().Be("Runs on the expression */20 9-17 * * 1-5, UTC. Checking it.");

        DateTimeOffset[] next = [start.AddHours(1), start.AddHours(1).AddMinutes(20)];
        fields.Render(set => set.Add(view => view.Next, next));

        fields.Find(".dv-when-reads").TextContent.Should().Be(
            $"Runs on the expression */20 9-17 * * 1-5, UTC. Next: {ScheduleWords.Moment(next[0])}; {ScheduleWords.Moment(next[1])}.");
        fields.Find("[role=alert]").TextContent.Trim().Should().BeEmpty();

        const string Refusal = "Schedules on this server run at most once every 60 minutes. Choose a time that repeats less often.";
        fields.Render(set => set.Add(view => view.ServerProblem, Refusal));

        fields.Find(".dv-when-reads").TextContent.Should().Be("The expression */20 9-17 * * 1-5 cannot be saved as it is.");
        fields.Find("[role=alert]").TextContent.Trim().Should().Be(Refusal);
    }

    #endregion
}
