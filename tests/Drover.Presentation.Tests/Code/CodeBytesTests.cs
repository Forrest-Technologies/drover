namespace Drover.Presentation.Tests.Code;

/// <summary>Text that is really bytes: when it is, a bytes literal's bytes, and the hex view.</summary>
public sealed class CodeBytesTests
{
    #region Binary

    [Theory]
    [InlineData("a\0b")]
    [InlineData("\u0001\u0002\u0003\u0004 data")]
    [InlineData("���abcdef")]
    public void Nul_control_characters_and_undecodable_text_are_binary(string text) =>
        CodeBytes.IsBinary(text).Should().BeTrue();

    [Theory]
    [InlineData("col\tcol\r\nline\n")]
    [InlineData("\u001b[32mgreen\u001b[0m and \u001b]0;title\u0007 rest of a long enough line for the share")]
    [InlineData("progress 10%\b\b\b20%\f")]
    [InlineData("café � here is a lot of plain text around one bad character")]
    public void Tabs_line_breaks_terminal_colors_and_a_rare_bad_character_are_text(string text) =>
        CodeBytes.IsBinary(text).Should().BeFalse();

    #endregion

    #region Literals

    [Fact]
    public void A_bytes_literal_reads_its_hex_and_named_escapes_and_printable_characters()
    {
        CodeBytes.Literal("b\"a\\x00\\n\\t\\\\\\\"z\\xff\"").Should().Equal([(byte)'a', 0, (byte)'\n', (byte)'\t', (byte)'\\', (byte)'"', (byte)'z', 0xff]);
    }

    [Theory]
    [InlineData("b'plain words'")]
    [InlineData("here is a long sentence that only mentions b'\\x01' in passing")]
    [InlineData("b'\\x1'")]
    [InlineData("b'\\xzz'")]
    [InlineData("b'café \\x01'")]
    [InlineData("'\\x01'")]
    public void Text_that_is_not_mostly_a_bytes_literal_with_hex_escapes_is_none(string text) =>
        CodeBytes.Literal(text).Should().BeNull();

    #endregion

    #region Hex View

    [Fact]
    public void The_hex_view_shows_sixteen_bytes_a_line_with_offsets_and_printable_characters()
    {
        byte[] bytes = [.. Encoding.ASCII.GetBytes("Hello, hex view!"), 0x00, 0xfe];

        var lines = CodeBytes.HexLines(bytes);

        lines.Should().HaveCount(2);
        lines[0].Should().Be(new HexLine("00000000", "48 65 6c 6c 6f 2c 20 68  65 78 20 76 69 65 77 21", "Hello, hex view!"));
        lines[1].Should().Be(new HexLine("00000010", "00 fe", ".."));
    }

    [Fact]
    public void No_bytes_show_no_lines() => CodeBytes.HexLines([]).Should().BeEmpty();

    [Theory]
    [InlineData(1, "1 byte")]
    [InlineData(2048, "2,048 bytes")]
    public void A_size_is_said_in_bytes(int bytes, string words) => CodeBytes.Size(bytes).Should().Be(words);

    #endregion
}
