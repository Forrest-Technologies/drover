namespace Drover.Presentation.Tests.Markdown;

/// <summary>The text find matches is the text the view draws: markup is gone, styles do not split words, and line
/// breaks and task prefixes are characters in the drawn text.</summary>
public sealed class MarkdownTextTests
{
    #region Public Methods

    [Theory]
    [InlineData("plain **bold** and `code`", "plain bold and code")]
    [InlineData("a  \nb", "a\nb")]
    [InlineData("[label](https://example.test/path)", "label")]
    [InlineData("![alt](https://example.test/a.png)", "Load image from example.test: alt")]
    [InlineData("- [x] write tests", "Done: write tests")]
    [InlineData("- [ ] write tests", "Not done: write tests")]
    [InlineData("```cs\nvar x = 1;\n```", "var x = 1;")]
    [InlineData("Cafe\u0301 **bold**", "Cafe\u0301 bold")]
    [InlineData("> quoted words", "quoted words")]
    [InlineData("text <span attribute=\"value\">stays text</span>", "text <span attribute=\"value\">stays text</span>")]
    public void The_drawn_text_of_one_block_matches_the_view(string source, string expected)
    {
        var blocks = MarkdownText.Blocks(MarkdownDocument.Parse(source));

        blocks.Should().ContainSingle().Which.Text.Should().Be(expected);
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
    public void Inline_code_carries_the_machine_style_of_the_view()
    {
        var block = MarkdownText.Blocks(MarkdownDocument.Parse("run `code` now")).Should().ContainSingle().Subject;

        block.Spans.Should().ContainSingle(span => span.Text == "code" && span.Style.Machine);
    }

    [Fact]
    public void A_table_keeps_cell_text_and_the_line_breaks_between_rows()
    {
        var text = """
            | A | B |
            | --- | --- |
            | 1 | 2 |
            """;

        var block = MarkdownText.Blocks(MarkdownDocument.Parse(text)).Should().ContainSingle().Subject;

        block.Text.Should().Be("A\tB\n1\t2");
    }

    [Fact]
    public void Whitespace_between_blocks_does_not_become_a_block()
    {
        MarkdownText.Blocks(MarkdownDocument.Parse("  \n\n")).Should().BeEmpty();
    }

    #endregion
}
