namespace Drover.Ui.Tests;

/// <summary>
/// The pure parts of the sandbox screen: where a tap lands on the sandbox's own screen at every size the picture can
/// be shown at, how the keyboard moves the pointer, the state in words, and how long the screen waits between pictures.
/// </summary>
public sealed class SandboxWordsTests
{
    #region Where A Tap Lands

    [Theory]
    [InlineData(328, 246, 164, 123, 512, 384)]
    [InlineData(328, 246, 0, 0, 0, 0)]
    [InlineData(328, 246, 328, 246, 1023, 767)]
    [InlineData(328, 246, 327.9, 245.9, 1023, 767)]
    [InlineData(380, 285, 95, 71.25, 256, 192)]
    [InlineData(1024, 768, 700, 500, 700, 500)]
    [InlineData(2048, 1536, 701, 501, 350, 250)]
    public void A_tap_on_the_shown_picture_lands_on_the_same_place_of_the_sandbox_screen(
        double shownWidth,
        double shownHeight,
        double offsetX,
        double offsetY,
        int x,
        int y)
    {
        ScreenPoint.FromShown(offsetX, offsetY, shownWidth, shownHeight, 1024, 768).Should().Be((x, y));
    }

    [Theory]
    [InlineData(-5, -5, 0, 0)]
    [InlineData(400, 300, 1023, 767)]
    public void A_tap_past_the_picture_edge_stays_on_the_screen(double offsetX, double offsetY, int x, int y)
    {
        ScreenPoint.FromShown(offsetX, offsetY, 328, 246, 1024, 768).Should().Be((x, y));
    }

    [Fact]
    public void A_picture_with_no_size_yet_maps_to_the_corner_instead_of_failing()
    {
        ScreenPoint.FromShown(10, 10, 0, 0, 1024, 768).Should().Be((0, 0));
    }

    [Theory]
    [InlineData("ArrowRight", false, 528, 384)]
    [InlineData("ArrowLeft", true, 448, 384)]
    [InlineData("ArrowUp", false, 512, 368)]
    [InlineData("ArrowDown", true, 512, 448)]
    public void An_arrow_key_moves_the_pointer_by_a_step_or_with_shift_by_a_long_one(string key, bool far, int x, int y)
    {
        ScreenPoint.Move((512, 384), key, far, 1024, 768).Should().Be((x, y));
    }

    [Fact]
    public void The_pointer_stops_at_the_screen_edge_and_other_keys_do_not_move_it()
    {
        ScreenPoint.Move((1020, 2), "ArrowRight", true, 1024, 768).Should().Be((1023, 2));
        ScreenPoint.Move((1020, 2), "ArrowUp", true, 1024, 768).Should().Be((1020, 0));
        ScreenPoint.Move((1020, 2), "Enter", false, 1024, 768).Should().BeNull();
    }

    [Fact]
    public void The_pointer_is_drawn_over_the_middle_of_its_pixel()
    {
        var (left, top) = ScreenPoint.Percent((512, 384), 1024, 768);

        left.Should().BeApproximately(50.05, 0.01);
        top.Should().BeApproximately(50.07, 0.01);
    }

    #endregion

    #region Words

    [Theory]
    [InlineData("none", "No sandbox yet", BadgeKind.Neutral)]
    [InlineData("starting", "Starting", BadgeKind.Warn)]
    [InlineData("running", "Running", BadgeKind.Ok)]
    [InlineData("paused", "Paused", BadgeKind.Neutral)]
    [InlineData("replacing", "Being replaced", BadgeKind.Warn)]
    public void Every_state_is_said_in_words(string state, string words, BadgeKind badge)
    {
        SandboxWords.State(state).Should().Be(words);
        SandboxWords.Badge(state).Should().Be(badge);
    }

    [Fact]
    public void The_last_use_is_said_as_a_person_would()
    {
        var now = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        SandboxWords.LastUsed(null, now).Should().Be("Not used yet");
        SandboxWords.LastUsed(now.AddSeconds(-20), now).Should().Be("Used just now");
        SandboxWords.LastUsed(now.AddSeconds(-90), now).Should().Be("Used a minute ago");
        SandboxWords.LastUsed(now.AddMinutes(-42), now).Should().Be("Used 42 minutes ago");
        SandboxWords.LastUsed(now.AddMinutes(-70), now).Should().Be("Used an hour ago");
        SandboxWords.LastUsed(now.AddHours(-5), now).Should().Be("Used 5 hours ago");
        SandboxWords.LastUsed(now.AddDays(-3), now).Should().StartWith("Used ").And.NotContain("ago");
    }

    [Fact]
    public void The_editor_line_says_the_state_and_the_last_use()
    {
        var now = DateTimeOffset.UtcNow;

        SandboxWords.Summary(new BotSandboxStatus(true, true, "running", now.AddMinutes(-5), false, false), now).Should().Be("Running · used 5 minutes ago");
        SandboxWords.Summary(new BotSandboxStatus(true, true, "none", null, false, false), now).Should().Be("No sandbox yet");
    }

    [Fact]
    public void A_command_outcome_says_its_exit_code_or_that_it_was_stopped()
    {
        SandboxWords.Outcome(new SandboxCommandResponse(2, string.Empty, "no", false, false, false)).Should().Be("Exit code 2.");
        SandboxWords.Outcome(new SandboxCommandResponse(null, string.Empty, string.Empty, true, true, true))
            .Should().Be("Stopped: it ran past the time limit. Some output was left out. This is a new sandbox; the old one and its files are gone.");
    }

    [Fact]
    public void The_screen_waits_two_seconds_and_longer_after_each_failure_up_to_thirty()
    {
        Enumerable.Range(0, 6).Select(SandboxWords.NextPictureAfter).Select(wait => wait.TotalSeconds)
            .Should().Equal(2, 4, 8, 16, 30, 30);
    }

    #endregion
}
