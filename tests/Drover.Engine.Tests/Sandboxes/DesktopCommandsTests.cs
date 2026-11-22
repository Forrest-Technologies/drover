namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// The commands behind the computer tools are programs and their arguments, never a shell line with a model's text in
/// it: coordinates are checked against the fixed screen, keys come from a fixed list, and typed text is one argument.
/// </summary>
public sealed class DesktopCommandsTests
{
    #region Arguments, Never A Shell

    [Fact]
    public void A_click_moves_and_clicks_in_one_xdotool_run()
    {
        var command = DesktopCommands.Click(10, 20, MouseButton.Left, twice: false);

        command.Program.Should().Be("xdotool");
        command.Arguments.Should().Equal("mousemove", "10", "20", "click", "1");
        command.Environment.Should().Contain("DISPLAY", ":0");
    }

    [Theory]
    [InlineData(MouseButton.Right, false, new[] { "mousemove", "1023", "767", "click", "3" })]
    [InlineData(MouseButton.Middle, false, new[] { "mousemove", "0", "0", "click", "2" })]
    [InlineData(MouseButton.Left, true, new[] { "mousemove", "512", "384", "click", "--repeat", "2", "1" })]
    public void Each_button_and_a_double_click_have_their_own_arguments(MouseButton button, bool twice, string[] expected)
    {
        var (x, y) = (int.Parse(expected[1], CultureInfo.InvariantCulture), int.Parse(expected[2], CultureInfo.InvariantCulture));

        DesktopCommands.Click(x, y, button, twice).Arguments.Should().Equal(expected);
    }

    [Theory]
    [InlineData("hello; rm -rf / && echo $(id)")]
    [InlineData("--window 1 'quoted' \"double\" `tick`")]
    [InlineData("line one\nline two\tafter a tab")]
    public void Typed_text_is_one_argument_after_the_end_of_options_however_it_looks(string text)
    {
        var command = DesktopCommands.Type(text);

        command.Program.Should().Be("xdotool");
        command.Arguments.Should().Equal("type", "--delay", "12", "--", text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bell\u0007")]
    [InlineData("escape\u001b[2J")]
    public void Text_that_is_empty_or_holds_control_characters_is_refused(string text)
    {
        var typing = () => DesktopCommands.Type(text);

        typing.Should().Throw<BuiltinToolException>();
    }

    [Fact]
    public void Text_longer_than_the_limit_is_refused()
    {
        var typing = () => DesktopCommands.Type(new string('a', DesktopCommands.MaxTypedCharacters + 1));

        typing.Should().Throw<BuiltinToolException>().WithMessage($"*{DesktopCommands.MaxTypedCharacters}*");
    }

    [Fact]
    public void Starting_the_display_holds_nothing_but_drovers_own_text()
    {
        var start = DesktopCommands.StartDisplay();

        start.Program.Should().Be("/bin/bash");
        start.Arguments[^1].Should().Contain("Xvfb :0 -ac -screen 0 1024x768x24").And.Contain("startxfce4").And.Contain("xdpyinfo -display :0");
        DesktopCommands.StartDisplay().Should().BeEquivalentTo(start, "nothing a caller passes can change it");
    }

    [Fact]
    public void A_session_that_has_been_up_a_while_is_not_waited_for_or_refused_while_a_part_of_it_restarts()
    {
        var script = DesktopCommands.StartDisplay().Arguments[^1];

        script.Should().Contain("""[ "${session_age:-0}" -ge 90 ] && exit 0""", "a screenshot every two seconds cannot wait a minute for a window manager");
        script.IndexOf("pgrep -x xfce4-session >/dev/null && exit 0", StringComparison.Ordinal)
            .Should().BeGreaterThan(script.IndexOf("seq 1 120", StringComparison.Ordinal), "a session that came up without its desktop still has a screen");
        script.TrimEnd().Should().EndWith("exit 4", "no session at all is still a failure");
    }

    [Fact]
    public void A_display_lock_left_by_a_display_that_is_gone_is_removed_before_a_new_one_starts()
    {
        var script = DesktopCommands.StartDisplay().Arguments[^1];

        var removal = script.IndexOf("pgrep -x Xvfb >/dev/null || rm -f /tmp/.X0-lock /tmp/.X11-unix/X0", StringComparison.Ordinal);
        removal.Should().BeGreaterThan(-1, "a sandbox resumed from disk keeps the lock of a display it no longer runs");
        removal.Should().BeLessThan(script.IndexOf("setsid -f Xvfb", StringComparison.Ordinal), "the lock is cleared before the display is started");
    }

    [Fact]
    public void Passwords_go_to_stdin_and_cannot_submit_the_remote_prompt()
    {
        var password = Guid.NewGuid().ToString("N");
        var command = DesktopCommands.TypePassword(password);
        command.Arguments.Should().Equal("type", "--clearmodifiers", "--delay", "12", "--file", "-");
        Encoding.UTF8.GetString(command.StandardInput!.Value.Span).Should().Be(password);
        command.ToString().Should().NotContain(password);

        var withEnter = () => DesktopCommands.TypePassword($"{password}\n");
        withEnter.Should().Throw<BuiltinToolException>();
    }

    [Fact]
    public void Opening_a_terminal_launches_a_detached_program_on_the_sandbox_display()
    {
        var command = DesktopCommands.OpenTerminal();
        command.Program.Should().Be("/bin/bash");
        command.Environment["DISPLAY"].Should().Be(":0");
        command.Arguments[^1].Should().Contain("setsid -f").And.Contain("xfce4-terminal");
    }

    [Fact]
    public void A_screenshot_and_its_removal_name_only_the_file()
    {
        DesktopCommands.Screenshot("/tmp/drover-screen-x.png").Arguments.Should().Equal("--pointer", "/tmp/drover-screen-x.png");
        DesktopCommands.Remove("/tmp/drover-screen-x.png").Arguments.Should().Equal("-f", "--", "/tmp/drover-screen-x.png");
    }

    #endregion

    #region The Pointer

    [Theory]
    [InlineData(MouseButton.Left, "1")]
    [InlineData(MouseButton.Right, "3")]
    [InlineData(MouseButton.Middle, "2")]
    public void A_press_moves_to_the_point_and_holds_the_button_down_in_one_xdotool_run(MouseButton button, string number)
    {
        var command = DesktopCommands.MouseDown(10, 20, button);

        command.Program.Should().Be("xdotool");
        command.Arguments.Should().Equal("mousemove", "10", "20", "mousedown", number);
        command.Environment.Should().Contain("DISPLAY", ":0");
    }

    [Fact]
    public void A_move_only_moves_the_pointer()
    {
        DesktopCommands.MouseMove(300, 400).Arguments.Should().Equal("mousemove", "300", "400");
    }

    [Fact]
    public void A_release_lets_go_of_the_button_where_the_pointer_is_or_at_a_point_when_one_is_given()
    {
        DesktopCommands.MouseUp(MouseButton.Right, null, null).Arguments.Should().Equal("mouseup", "3");
        DesktopCommands.MouseUp(MouseButton.Left, 7, 8).Arguments.Should().Equal("mousemove", "7", "8", "mouseup", "1");
    }

    [Fact]
    public void Letting_go_of_every_held_button_is_one_run_with_a_short_time_limit()
    {
        var command = DesktopCommands.Release([MouseButton.Left, MouseButton.Middle]);

        command.Arguments.Should().Equal("mouseup", "1", "mouseup", "2");
        command.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_release_of_no_button_is_refused()
    {
        ((Func<CommandRequest>)(() => DesktopCommands.Release([]))).Should().Throw<BuiltinToolException>();
    }

    [Fact]
    public void A_drag_presses_moves_along_a_bounded_line_and_releases_in_one_run()
    {
        var command = DesktopCommands.Drag(100, 200, 500, 600, MouseButton.Left);

        command.Program.Should().Be("xdotool");
        command.Arguments.Take(5).Should().Equal("mousemove", "100", "200", "mousedown", "1");
        command.Arguments.TakeLast(2).Should().Equal("mouseup", "1");
        command.Arguments.Skip(5).Where(argument => argument == "mousemove").Should().HaveCount(DesktopCommands.DragSteps + 1);
        command.Arguments.Should().ContainInOrder("mousemove", "500", "600", "sleep", "mouseup");
        command.Arguments.Count(argument => argument == "mousedown").Should().Be(1);
        command.Arguments.Count(argument => argument == "mouseup").Should().Be(1);
    }

    [Fact]
    public void A_drag_goes_through_evenly_spaced_points_that_are_all_on_the_screen()
    {
        var command = DesktopCommands.Drag(0, 767, 1023, 0, MouseButton.Left);

        var points = command.Arguments
            .Select((argument, index) => (argument, index))
            .Where(item => item.argument == "mousemove")
            .Select(item => (X: int.Parse(command.Arguments[item.index + 1], CultureInfo.InvariantCulture), Y: int.Parse(command.Arguments[item.index + 2], CultureInfo.InvariantCulture)))
            .ToList();

        points.Should().HaveCount(DesktopCommands.DragSteps + 2);
        points.Should().OnlyContain(point => point.X >= 0 && point.X < DesktopCommands.ScreenWidth && point.Y >= 0 && point.Y < DesktopCommands.ScreenHeight);
        points[0].Should().Be((0, 767));
        points[^1].Should().Be((1023, 0));
        points.Select(point => point.X).Should().BeInAscendingOrder();
        points.Select(point => point.Y).Should().BeInDescendingOrder();
    }

    [Fact]
    public void A_drag_has_time_to_finish_within_its_command_limit()
    {
        var command = DesktopCommands.Drag(0, 0, 1023, 767, MouseButton.Left);

        command.Timeout.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(30));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1024, 0)]
    [InlineData(0, 768)]
    public void A_pointer_point_off_the_screen_is_refused_in_a_plain_sentence(int x, int y)
    {
        ((Func<CommandRequest>)(() => DesktopCommands.MouseDown(x, y, MouseButton.Left))).Should().Throw<BuiltinToolException>().WithMessage("*off the screen*");
        ((Func<CommandRequest>)(() => DesktopCommands.MouseMove(x, y))).Should().Throw<BuiltinToolException>().WithMessage("*off the screen*");
        ((Func<CommandRequest>)(() => DesktopCommands.MouseUp(MouseButton.Left, x, y))).Should().Throw<BuiltinToolException>().WithMessage("*off the screen*");
        ((Func<CommandRequest>)(() => DesktopCommands.Drag(x, y, 5, 5, MouseButton.Left))).Should().Throw<BuiltinToolException>().WithMessage("*off the screen*");
        ((Func<CommandRequest>)(() => DesktopCommands.Drag(5, 5, x, y, MouseButton.Left))).Should().Throw<BuiltinToolException>().WithMessage("*off the screen*");
    }

    [Fact]
    public void A_release_with_half_a_point_is_refused()
    {
        ((Func<CommandRequest>)(() => DesktopCommands.MouseUp(MouseButton.Left, 10, null))).Should().Throw<BuiltinToolException>();
    }

    #endregion

    #region Keys

    [Fact]
    public void Keys_pressed_together_become_one_chord_of_key_symbols()
    {
        DesktopCommands.Key(["ctrl", "L"]).Arguments.Should().Equal("key", "Control_L+l");
        DesktopCommands.Key(["Enter"]).Arguments.Should().Equal("key", "Return");
        DesktopCommands.Key(["shift", "alt", "f4"]).Arguments.Should().Equal("key", "Shift_L+Alt_L+F4");
    }

    [Theory]
    [InlineData("Return; reboot")]
    [InlineData("ctrl+alt")]
    [InlineData("XF86PowerOff")]
    [InlineData("--clearmodifiers")]
    [InlineData("é")]
    public void A_key_name_not_on_the_list_is_refused(string name)
    {
        var pressing = () => DesktopCommands.Key(["ctrl", name]);

        pressing.Should().Throw<BuiltinToolException>().WithMessage("There is no key named*");
    }

    [Fact]
    public void No_keys_or_too_many_at_once_are_refused()
    {
        ((Func<CommandRequest>)(() => DesktopCommands.Key([]))).Should().Throw<BuiltinToolException>();
        ((Func<CommandRequest>)(() => DesktopCommands.Key(["a", "b", "c", "d", "e", "f"]))).Should().Throw<BuiltinToolException>();
    }

    #endregion

    #region The Screen

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1024, 0)]
    [InlineData(0, 768)]
    [InlineData(5000, 5000)]
    public void A_point_off_the_screen_is_refused(int x, int y)
    {
        ((Func<CommandRequest>)(() => DesktopCommands.Click(x, y, MouseButton.Left, false))).Should().Throw<BuiltinToolException>().WithMessage("*off the screen*");
        ((Func<CommandRequest>)(() => DesktopCommands.Scroll(ScrollDirection.Down, 1, x, y))).Should().Throw<BuiltinToolException>().WithMessage("*off the screen*");
    }

    [Fact]
    public void Scrolling_clicks_the_wheel_at_a_point_or_where_the_pointer_is()
    {
        DesktopCommands.Scroll(ScrollDirection.Up, 3, null, null).Arguments.Should().Equal("click", "--repeat", "3", "4");
        DesktopCommands.Scroll(ScrollDirection.Down, 2, 100, 200).Arguments.Should().Equal("mousemove", "100", "200", "click", "--repeat", "2", "5");
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(21, null, null)]
    [InlineData(3, 10, null)]
    public void A_scroll_with_a_wrong_amount_or_half_a_point_is_refused(int amount, int? x, int? y)
    {
        ((Func<CommandRequest>)(() => DesktopCommands.Scroll(ScrollDirection.Down, amount, x, y))).Should().Throw<BuiltinToolException>();
    }

    [Fact]
    public void The_size_of_a_png_is_read_from_its_header_and_anything_else_is_not_a_png()
    {
        DesktopCommands.ReadPngSize(TestPng.Make(1024, 768)).Should().Be((1024, 768));
        DesktopCommands.ReadPngSize("not a picture at all, just text"u8).Should().BeNull();
        DesktopCommands.ReadPngSize(TestPng.Make(1024, 768).AsSpan(0, 20)).Should().BeNull();
    }

    #endregion
}
