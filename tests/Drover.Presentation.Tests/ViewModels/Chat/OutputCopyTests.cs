namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>What copying a block puts on the clipboard: all of it, as written, with real tabs and line breaks.</summary>
public sealed class OutputCopyTests
{
    #region Copy Text

    [Fact]
    public void Copied_output_keeps_its_real_tabs_and_line_breaks_and_holds_every_piece_in_order()
    {
        var rows = "chain\tname\tprice\nsolana\tCYBERLEEK\t0.002139\n";
        IReadOnlyList<ToolOutputPiece> pieces = [new("stdout", rows), new(OutputWords.ErrorStream, "warning: slow\n"), new("stdout", "done\n")];

        var copied = OutputWords.CopyText(pieces);

        copied.Should().Be($"{rows}warning: slow\ndone\n");
        copied.Should().Contain("\t").And.NotContain("\\t").And.NotContain(OutputWords.ErrorStream, "stream tags are drawn, not printed");
    }

    [Fact]
    public void Copied_output_is_the_whole_text_however_long()
    {
        var line = $"{new string('x', 200)}\n";
        var whole = string.Concat(Enumerable.Repeat(line, 5_000));

        OutputWords.CopyText([new ToolOutputPiece("stdout", whole)]).Should().HaveLength(whole.Length);
    }

    [Fact]
    public void Nothing_printed_copies_nothing() => OutputWords.CopyText([]).Should().BeEmpty();

    [Fact]
    public void Code_and_output_blocks_name_their_copy_icon_in_words()
    {
        OutputWords.CopyCode.Should().Be("Copy code");
        OutputWords.CopyOutput.Should().Be("Copy output");
    }

    #endregion
}
