namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// A routine's row and the "When" fields: the cases of the web client's component tests, asked of what the native
/// screen shows them from.
/// </summary>
public sealed class RoutineRowViewModelTests
{
    #region Private Fields

    private static readonly DateTimeOffset start = new(2026, 1, 5, 8, 0, 0, TimeSpan.Zero);

    #endregion

    #region Starter Tests

    [Fact]
    public void A_routine_is_headed_by_its_sentence_and_only_those_who_may_change_it_get_controls()
    {
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.\nKeep it short.", "0 8 * * 1-5", "Europe/Berlin", true, start, null);
        var row = new RoutineRowViewModel { Routine = routine, BotName = "Sample bot" };

        row.Sentence.Should().Be("Every weekday at 08:00, Berlin time");
        row.Prompt.Should().Be("Write the brief.");
        row.CanChange.Should().BeFalse("only those who may change it get controls");
        row.ReadWholeShown.Should().BeFalse();

        row.CanChange = true;
        row.BotPaused = true;

        new[] { RoutineRowViewModel.RunOnce, RoutineRowViewModel.Change, RoutineRowViewModel.Remove }.Should().Equal("Run once now", "Change", "Remove");
        row.Next.Should().Be("Does not run while Sample bot is paused.");
        row.Last.Should().Be("Has not run yet");
    }

    [Fact]
    public void Choosing_when_shows_only_that_choices_fields_and_reads_the_schedule_back()
    {
        var when = RoutineWhen.Start;

        RoutineWhenFields.Reads(when, "Europe/Berlin", null, null).Should().Be("Reads as: Every weekday at 09:00, Berlin time.");
        RoutineWhenFields.AtShown(when).Should().BeTrue();

        when = RoutineWhenFields.ChooseRhythm(when, nameof(RoutineRhythm.ChosenDays)) with { Days = [] };

        RoutineWords.Week.Should().HaveCount(7);
        RoutineWhenFields.Reads(when, "Europe/Berlin", null, null).Should().Be("Choose at least one day.");

        when = RoutineWhenFields.ToggleDay(when, RoutineWords.Week[2], true);

        when.Days.Should().Equal(DayOfWeek.Wednesday);
    }

    #endregion

    #region Product

    [Fact]
    public void Only_the_routines_owner_may_change_it_and_every_control_is_named_with_its_sentence()
    {
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.", "0 8 * * 1-5", "Europe/Berlin", false, null, null);
        var row = new RoutineRowViewModel { Routine = routine, BotName = "Sample bot", CanChange = true, CanRewrite = false };

        row.CanRewrite.Should().BeFalse("only Run once now and Remove are offered");
        RoutineRowViewModel.OnlyTheMaker.Should().Be("Only the person who made it can change what it sends or when, because it runs as them.");
        RoutineRowViewModel.SwitchLabel.Should().Be("Runs on its own");
        row.SwitchAbout.Should().Be("Every weekday at 08:00, Berlin time: Write the brief.");
        row.Next.Should().Be("Off. It does not run until it is turned on.");
    }

    [Fact]
    public void Someone_who_did_not_write_a_routine_can_read_its_whole_prompt_and_its_times_are_in_its_zone()
    {
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.\nThen send it to everyone.", "0 8 * * *", "Asia/Tokyo", true, start, null);
        var row = new RoutineRowViewModel { Routine = routine, BotName = "Sample bot", CanRewrite = false };

        row.ReadWholeShown.Should().BeTrue();
        RoutineRowViewModel.ReadWholePrompt.Should().Be("Read the whole prompt");
        row.Prompt.Should().Be("Write the brief.");
        row.WholePrompt.Should().BeFalse();
        row.Next.Should().Be($"Runs {RoutineWords.Moment(start, "Asia/Tokyo")}");

        row.ToggleWholePrompt();

        row.Prompt.Should().Be("Write the brief.\nThen send it to everyone.");
        row.WholePrompt.Should().BeTrue();

        row.CanRewrite = true;

        row.ReadWholeShown.Should().BeFalse("the owner reads the whole prompt by changing it");
    }

    [Fact]
    public void A_refused_run_from_the_dialog_puts_focus_back_on_run_once_now_beside_the_refusal()
    {
        const string Refusal = "Sample bot is paused, so its routines do not run. Resume it first.";
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.", "0 8 * * *", "UTC", true, start, null);
        var row = new RoutineRowViewModel
        {
            Routine = routine,
            BotName = "Sample bot",
            CanChange = true,
            CanRewrite = false,
            StartedHref = "/threads/0196f1a0-0000-7000-8000-0000000000b1",
            Problem = Refusal,
            FocusAfterRun = 1,
        };

        row.TakeFocusAfterRun().Should().Be(RoutineFocus.RunOnce);
        row.Problem.Should().Be(Refusal, "the refusal is read out where focus lands");
    }

    [Fact]
    public void Each_run_from_the_dialog_moves_focus_to_its_conversation_even_when_it_is_the_same_one()
    {
        const string Main = "/threads/0196f1a0-0000-7000-8000-0000000000b2";
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.", "0 8 * * *", "UTC", true, start, null);
        var row = new RoutineRowViewModel { Routine = routine, BotName = "Sample bot", CanChange = true, StartedHref = Main, FocusAfterRun = 1 };

        var first = row.TakeFocusAfterRun();
        row.FocusAfterRun = 1;
        var again = row.TakeFocusAfterRun();
        row.FocusAfterRun = 2;
        var second = row.TakeFocusAfterRun();

        new[] { first, again, second }.Should().Equal([RoutineFocus.Started, null, RoutineFocus.Started], "one focus for each run, and none again for the same one");
    }

    [Fact]
    public void A_routine_run_once_links_to_its_conversation_and_a_refusal_is_said_in_the_row()
    {
        var routine = new RoutineItem(Guid.NewGuid(), "Write the brief.", "0 8 * * *", "UTC", true, start, null);
        var row = new RoutineRowViewModel
        {
            Routine = routine,
            BotName = "Sample bot",
            StartedHref = "/threads/0196f1a0-0000-7000-8000-0000000000b1",
            Problem = "Sample bot is paused, so its routines do not run. Resume it first.",
        };

        row.StartedHref.Should().Be("/threads/0196f1a0-0000-7000-8000-0000000000b1");
        RoutineRowViewModel.OpenStarted.Should().Be("Open the conversation it started");
        row.Problem.Should().Be("Sample bot is paused, so its routines do not run. Resume it first.");
    }

    [Fact]
    public void A_custom_expression_is_explained_by_its_next_times_or_refused_with_the_servers_sentence()
    {
        var when = RoutineWhen.Start with { Rhythm = RoutineRhythm.Custom, Cron = "*/20 9-17 * * 1-5" };

        RoutineWhenFields.Reads(when, "UTC", null, null).Should().Be("Runs on the expression */20 9-17 * * 1-5, UTC. Checking it.");
        RoutineWhenFields.AtShown(when).Should().BeFalse();

        DateTimeOffset[] next = [start.AddHours(1), start.AddHours(1).AddMinutes(20)];

        RoutineWhenFields.Reads(when, "UTC", null, next).Should().Be(
            $"Runs on the expression */20 9-17 * * 1-5, UTC. Next: {RoutineWords.Moment(next[0], "UTC")}; {RoutineWords.Moment(next[1], "UTC")}.");

        const string Refusal = "Schedules on this server run at most once every 60 minutes. Choose a time that repeats less often.";

        RoutineWhenFields.Reads(when, "UTC", Refusal, next).Should().Be("The expression */20 9-17 * * 1-5 cannot be saved as it is.");
    }

    [Fact]
    public void A_cron_the_editor_cannot_word_is_shown_beside_its_sentence_as_the_expression_it_is()
    {
        var custom = new RoutineRowViewModel { Routine = new(Guid.NewGuid(), "Write the brief.", "*/20 9-17 * * 1-5", "UTC", true, start, null), BotName = "Sample bot" };
        var worded = new RoutineRowViewModel { Routine = new(Guid.NewGuid(), "Write the brief.", "0 8 * * *", "UTC", true, start, null), BotName = "Sample bot" };

        custom.Cron.Should().Be("*/20 9-17 * * 1-5");
        worded.Cron.Should().BeNull();
    }

    #endregion
}
