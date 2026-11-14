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
    public void Starting_the_display_is_the_only_shell_line_and_holds_nothing_but_drovers_own_text()
    {
        var start = DesktopCommands.StartDisplay();

        start.Program.Should().Be("/bin/bash");
        start.Arguments[^1].Should().Contain("Xvfb :0 -ac -screen 0 1024x768x24").And.Contain("startxfce4").And.Contain("xdpyinfo -display :0");
        DesktopCommands.StartDisplay().Should().BeEquivalentTo(start, "nothing a caller passes can change it");
    }

    [Fact]
    public void A_screenshot_and_its_removal_name_only_the_file()
    {
        DesktopCommands.Screenshot("/tmp/drover-screen-x.png").Arguments.Should().Equal("--pointer", "/tmp/drover-screen-x.png");
        DesktopCommands.Remove("/tmp/drover-screen-x.png").Arguments.Should().Equal("-f", "--", "/tmp/drover-screen-x.png");
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
