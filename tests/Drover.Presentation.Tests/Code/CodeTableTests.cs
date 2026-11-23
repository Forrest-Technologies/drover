namespace Drover.Presentation.Tests.Code;

/// <summary>Tab and comma separated rows read as a table: quotes, empty cells, the header row, and numeric columns.</summary>
public sealed class CodeTableTests
{
    #region Reading

    [Fact]
    public void Csv_cells_in_quotes_hold_commas_doubled_quotes_and_line_breaks()
    {
        var table = CodeTable.Read("name,quote\n\"Smith, Ann\",\"She said \"\"hi\"\"\"\nBo,\"two\nlines\"\n");

        table.Should().NotBeNull();
        table!.Header.Should().Equal("name", "quote");
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Should().Equal("Smith, Ann", "She said \"hi\"");
        table.Rows[1].Should().Equal("Bo", "two\nlines");
    }

    [Fact]
    public void Empty_cells_and_a_trailing_line_break_are_kept_and_dropped_as_written()
    {
        var table = CodeTable.Read("a\tb\tc\n1\t\t3\r\n4\t5\t\n\n");

        table!.Rows.Should().HaveCount(2);
        table.Rows[0].Should().Equal("1", string.Empty, "3");
        table.Rows[1].Should().Equal("4", "5", string.Empty);
    }

    [Theory]
    [InlineData("a\tb\nc\td\te\n")]
    [InlineData("only\tone row\n")]
    [InlineData("one column\nper line\n")]
    [InlineData("Hello, world.\nThis is, in fact, prose.\n")]
    [InlineData("Well, I think so\n")]
    [InlineData("\"unclosed, quote\nx,y\n")]
    public void Ragged_rows_single_rows_single_columns_prose_and_open_quotes_are_no_table(string text) =>
        CodeTable.Read(text).Should().BeNull();

    #endregion

    #region Shape

    [Fact]
    public void A_first_row_of_names_is_the_header_and_numeric_columns_are_marked()
    {
        var table = CodeTable.Read("chain\tname\tprice\nsolana\tCYBERLEEK\t0.002139\nbase\tTOSHI\t1,200\n")!;

        table.Header.Should().Equal("chain", "name", "price");
        table.Numeric.Should().Equal(false, false, true);
        table.ColumnCount.Should().Be(3);
    }

    [Fact]
    public void A_first_row_holding_a_number_is_data_not_a_header()
    {
        var table = CodeTable.Read("1\t2\n3\t4\n")!;

        table.Header.Should().BeNull();
        table.Rows.Should().HaveCount(2);
        table.Numeric.Should().Equal(true, true);
        table.Label.Should().Be("Table, 2 rows");
    }

    [Fact]
    public void A_column_with_any_word_is_not_numeric_and_empty_cells_do_not_decide()
    {
        var table = CodeTable.Read("id\tcount\tnote\n1\t\tok\n2\t5\t-\nx\t7\t3\n")!;

        table.Numeric.Should().Equal(false, true, false);
    }

    [Theory]
    [InlineData("12", true)]
    [InlineData("-0.5", true)]
    [InlineData("1,200", true)]
    [InlineData("3e8", true)]
    [InlineData(" 7 ", true)]
    [InlineData("v1", false)]
    [InlineData("", false)]
    [InlineData("0x1f", false)]
    public void A_cell_is_a_number_as_written_in_the_invariant_culture(string cell, bool number) =>
        CodeTable.IsNumber(cell).Should().Be(number);

    #endregion
}
