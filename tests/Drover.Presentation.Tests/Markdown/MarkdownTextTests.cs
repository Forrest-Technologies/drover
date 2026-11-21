namespace Drover.Presentation.Tests.Markdown;

/// <summary>The text find matches is the text the view draws: markup is gone, styles do not split words, code keeps
/// its exact characters, and line breaks and task prefixes are characters in the drawn text.</summary>
public sealed class MarkdownTextTests
{
    #region Public Methods

    [Theory]
    [InlineData("plain **bold** and `code`", "plain bold and code")]
    [InlineData("[label](https://example.test/path)", "label")]
    [InlineData("![alt](https://example.test/a.png)", "Load image from example.test: alt")]
    [InlineData("- [x] write tests", "Done: write tests")]
    [InlineData("- [ ] write tests", "Not done: write tests")]
    [InlineData("Cafe\u0301 **bold**", "Cafe\u0301 bold")]
    [InlineData("text <span attribute=\"value\">stays text</span>", "text <span attribute=\"value\">stays text</span>")]
    [InlineData("# A heading", "A heading")]
    [InlineData("- first\n- second", "first\nsecond")]
    [InlineData("1. first\n2. second", "first\nsecond")]
    [InlineData("- first\n  - nested", "first\n\nnested")]
    [InlineData("> first\n>\n> second", "first\nsecond")]
    public void The_drawn_text_of_one_block_matches_the_view(string source, string expected)
    {
        var blocks = MarkdownText.Blocks(MarkdownDocument.Parse(source));

        blocks.Should().ContainSingle().Which.Text.Should().Be(expected);
    }

    [Fact]
    public void Code_keeps_its_indentation_and_blank_lines()
    {
        var source = "```py\nif True:\n    x  =  1\n\n    print(x)\n```";

        var block = MarkdownText.Blocks(MarkdownDocument.Parse(source)).Should().ContainSingle().Subject;

        block.Text.Should().Be("if True:\n    x  =  1\n\n    print(x)");
        block.Spans.Should().ContainSingle().Which.Style.Machine.Should().BeTrue();
    }

    [Fact]
    public void Double_spaces_in_inline_code_are_kept()
    {
        var block = MarkdownText.Blocks(MarkdownDocument.Parse("use `x  =  1` now")).Should().ContainSingle().Subject;

        block.Spans.Should().ContainSingle(span => span.Text == "x  =  1" && span.Style.Machine);
        block.Text.Should().Be("use x  =  1 now");
    }

    [Fact]
    public void A_table_keeps_empty_cells_and_the_line_breaks_between_rows()
    {
        var text = """
            | A | B |
            | --- | --- |
            | 1 |  |
            """;

        var block = MarkdownText.Blocks(MarkdownDocument.Parse(text)).Should().ContainSingle().Subject;

        block.Text.Should().Be("A\tB\n1\t");
    }

    [Fact]
    public void Table_rows_join_cells_and_rows_consistently()
    {
        var text = """
            | A | B |
            | --- | --- |
            | 1 |  |
            | 3 | 4 |
            """;

        var block = MarkdownText.Blocks(MarkdownDocument.Parse(text)).Should().ContainSingle().Subject;

        block.Text.Should().Be("A\tB\n1\t\n3\t4");
    }

    [Fact]
    public void A_rule_between_blocks_does_not_add_text()
    {
        var blocks = MarkdownText.Blocks(MarkdownDocument.Parse("before\n\n---\n\nafter"));

        blocks.Select(block => block.Text).Should().Equal("before", "after");
    }

    [Fact]
    public void A_word_split_by_bold_and_plain_text_stays_one_piece()
    {
        var block = MarkdownText.Blocks(MarkdownDocument.Parse("**bold**ly")).Should().ContainSingle().Subject;

        block.Text.Should().Be("boldly");
        block.Spans.Select(span => span.Text).Should().Equal("bold", "ly");
        block.Spans[0].Style.Strong.Should().BeTrue();
        block.Spans[1].Style.Strong.Should().BeFalse();
    }

    [Fact]
    public void Whitespace_between_blocks_does_not_become_a_block()
    {
        MarkdownText.Blocks(MarkdownDocument.Parse("  \n\n")).Should().BeEmpty();
    }

    #endregion
}
