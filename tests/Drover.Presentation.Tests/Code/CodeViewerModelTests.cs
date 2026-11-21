namespace Drover.Presentation.Tests.Code;

/// <summary>The full screen view of a block: its name, its own wrap choice, and closing it by Back or by itself.</summary>
public sealed class CodeViewerModelTests
{
    #region Private Fields

    private readonly Navigator navigator = new();

    #endregion

    #region Title

    [Theory]
    [InlineData("chain\tname\nsolana\tCYBER\nbase\tTOSHI\n", null, "Full screen: Table, 2 rows")]
    [InlineData("{\"a\":1}", null, "Full screen: JSON")]
    [InlineData("ls -la", "bash", "Full screen: bash")]
    [InlineData("hello", "Output", "Full screen: Output")]
    [InlineData("b'\\x01\\x02'", null, "Full screen: Bytes, 2")]
    public void The_view_is_named_by_the_kind_of_block_it_shows(string text, string? language, string title)
    {
        var viewer = new CodeViewerModel(navigator);

        viewer.Show(CodeContent.Detect(text, language), blockWraps: false);

        viewer.Title.Should().Be(title);
    }

    #endregion

    #region Wrap

    [Fact]
    public void The_view_opens_on_the_blocks_wrap_choice_and_keeps_its_own_after()
    {
        var viewer = new CodeViewerModel(navigator);

        viewer.Show(CodeContent.Detect("text"), blockWraps: true);
        viewer.Wraps.Should().BeTrue();

        viewer.ToggleWrap();

        viewer.Wraps.Should().BeFalse("the change is the view's own; the block keeps its choice");
    }

    #endregion

    #region Closing

    [Fact]
    public void While_open_a_step_back_closes_the_view_and_nothing_else()
    {
        navigator.NavigateTo(Addresses.Bots);
        var viewer = new CodeViewerModel(navigator);
        var closed = 0;
        viewer.Closed += () => closed++;

        viewer.Show(CodeContent.Detect("text"), blockWraps: false);

        navigator.StepBack().Should().Be(BackStep.Closed);
        viewer.IsOpen.Should().BeFalse();
        closed.Should().Be(1);
        navigator.Current.Should().Be(Addresses.Bots);
    }

    [Fact]
    public void Closing_the_view_itself_takes_its_closer_off_the_navigator()
    {
        navigator.NavigateTo(Addresses.Home);
        navigator.NavigateTo(Addresses.Bots);
        var viewer = new CodeViewerModel(navigator);
        var closed = 0;
        viewer.Closed += () => closed++;
        viewer.Show(CodeContent.Detect("text"), blockWraps: false);

        viewer.Close();
        viewer.Close();

        closed.Should().Be(1, "closing twice closes once");
        navigator.StepBack().Should().Be(BackStep.WentBack, "nothing is left open to close");
    }

    [Fact]
    public void Showing_another_block_while_open_registers_the_closer_once()
    {
        navigator.NavigateTo(Addresses.Home);
        navigator.NavigateTo(Addresses.Bots);
        var viewer = new CodeViewerModel(navigator);

        viewer.Show(CodeContent.Detect("a"), blockWraps: false);
        viewer.Show(CodeContent.Detect("b"), blockWraps: false);

        navigator.StepBack().Should().Be(BackStep.Closed);
        navigator.StepBack().Should().Be(BackStep.WentBack);
    }

    [Fact]
    public void The_view_names_its_buttons_in_words()
    {
        CodeViewerModel.OpenLabel.Should().Be("Open full screen");
        CodeViewerModel.CloseLabel.Should().Be("Close full screen");
    }

    #endregion
}
