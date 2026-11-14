namespace Drover.Ui.Tests;

/// <summary>
/// The phone fixes that only a browser can draw are held to the source that ships: each narrow rule is present, uses
/// the touch token and not a number, and is kept inside a query, so it cannot leak to a wide screen. The sizes were
/// measured in a real browser at 360 and 412 px.
/// </summary>
public sealed class PhoneLayoutTests
{
    #region Private Fields

    private const string TouchQuery = "@media (pointer: coarse)";

    private const string PhoneQuery = "@media (max-width: 559px)";

    #endregion

    #region Public Methods

    [Fact]
    public void The_work_bar_keeps_one_row_on_a_phone_and_the_account_name_gives_way_with_an_ellipsis()
    {
        var styles = After(Source("Layouts", "WorkLayout.razor.css"), PhoneQuery);

        styles.Should().Contain("flex-wrap: nowrap;");
        styles.Should().Contain("text-overflow: ellipsis;");
        Before(Source("Layouts", "WorkLayout.razor.css"), PhoneQuery).Should().Contain("flex-wrap: wrap;", "wider screens keep the wrapping bar");
    }

    [Fact]
    public void The_unsaved_changes_bar_keeps_one_row_on_a_phone_unless_it_carries_a_problem()
    {
        var styles = After(Source("Pages", "Bots", "BotEditor.razor.css"), PhoneQuery);

        styles.Should().Contain("flex-wrap: nowrap;");
        styles.Should().Contain(".dv-editor-save-problem {");
    }

    [Theory]
    [InlineData("Components", "Switch.razor.css", ".dv-switch-input")]
    [InlineData("Pages", "Bots/BotList.razor.css", ".dv-bot-name")]
    [InlineData("Pages", "Bots/BotEditor.razor.css", ".dv-editor-hint a")]
    public void A_control_that_is_smaller_than_a_finger_grows_to_the_touch_size_only_for_a_coarse_pointer(
        string folder, string file, string selector)
    {
        var styles = Source(folder, file);

        var touch = After(styles, TouchQuery);
        touch.Should().Contain(selector);
        touch.Should().Contain("var(--dv-target-touch)");
        styles.Should().NotContain("44px", "the touch size comes from the token");
    }

    [Fact]
    public void The_dialog_script_wraps_Tab_and_Shift_Tab_inside_the_modal_dialog()
    {
        var script = Source("Components", "Dialog.razor.js");

        script.Should().Contain("showModal()");
        script.Should().Contain("addEventListener(\"keydown\"");
        script.Should().Contain("event.shiftKey");
        script.Should().Contain("last.focus()").And.Contain("first.focus()");
    }

    #endregion

    #region Private Methods

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepositoryFiles.Root, "src", "Drover.Ui", .. parts]));

    private static string After(string text, string marker) => text[text.IndexOf(marker, StringComparison.Ordinal)..];

    private static string Before(string text, string marker) => text[..text.IndexOf(marker, StringComparison.Ordinal)];

    #endregion
}
