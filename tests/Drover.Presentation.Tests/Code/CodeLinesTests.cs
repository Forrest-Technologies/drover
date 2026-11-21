namespace Drover.Presentation.Tests.Code;

/// <summary>Reading a block line by line: diffs, logs, and the cap on what is laid out at once.</summary>
public sealed class CodeLinesTests
{
    #region Diffs

    [Theory]
    [InlineData("+++ b/x", DiffLineKind.Header)]
    [InlineData("--- a/x", DiffLineKind.Header)]
    [InlineData("diff --git a/x b/x", DiffLineKind.Header)]
    [InlineData("@@ -1 +1 @@", DiffLineKind.Hunk)]
    [InlineData("+added", DiffLineKind.Added)]
    [InlineData("-removed", DiffLineKind.Removed)]
    [InlineData(" same", DiffLineKind.Context)]
    public void Each_diff_line_is_known_by_its_start(string line, DiffLineKind kind) => CodeLines.DiffKindOf(line).Should().Be(kind);

    [Fact]
    public void A_list_of_dashes_is_no_diff()
    {
        CodeLines.IsDiff(["- milk", "- eggs", "- bread"]).Should().BeFalse("there is no diff header");
        CodeLines.IsDiff(["@@ -1 +1 @@", " same"]).Should().BeFalse("nothing was added or removed");
    }

    #endregion

    #region Logs

    [Fact]
    public void A_log_lines_time_and_level_word_are_found_with_their_severity()
    {
        var line = "2026-11-20 10:00:03,120 [ERROR] failed to bind";

        var read = CodeLines.LogLineOf(line);

        read.Starts.Should().BeTrue();
        line[read.Time!.Value].Should().Be("2026-11-20 10:00:03,120");
        line[read.Level!.Value].Should().Be("ERROR");
        read.Severity.Should().Be(LogSeverity.Failure);
    }

    [Theory]
    [InlineData("WARN disk is slow", LogSeverity.Attention)]
    [InlineData("12:00:01 DEBUG x", LogSeverity.Quiet)]
    [InlineData("nothing to see", LogSeverity.None)]
    public void A_level_word_gives_its_severity(string line, LogSeverity severity) =>
        CodeLines.LogLineOf(line).Severity.Should().Be(severity);

    [Fact]
    public void Lines_that_merely_mention_a_level_word_are_no_log()
    {
        CodeLines.IsLog(["The ERROR came later", "and so did the WARN"]).Should().BeFalse();
        CodeLines.IsLog(["INFO one line only"]).Should().BeFalse();
        CodeLines.IsLog(["INFO a", "WARN b", "plain"]).Should().BeTrue();
    }

    #endregion

    #region Cap

    [Fact]
    public void A_short_block_is_laid_out_whole()
    {
        var capped = CodeLines.Capped("a\nb\n");

        capped.Should().Be(new CappedText("a\nb\n", 2, false));
    }

    [Fact]
    public void Past_the_line_cap_only_the_first_lines_are_laid_out_and_all_are_counted()
    {
        var text = string.Concat(Enumerable.Range(1, 1_200).Select(line => $"line {line}\n"));

        var capped = CodeLines.Capped(text);

        capped.IsCut.Should().BeTrue();
        capped.TotalLines.Should().Be(1_200);
        CodeLines.LineCount(capped.Shown).Should().Be(CodeLines.MaxLines);
        capped.Shown.Should().StartWith("line 1\n").And.EndWith("line 400\n");
        CodeLines.ShowAll(capped.TotalLines).Should().Be("Show all 1,200 lines");
    }

    [Fact]
    public void Past_the_size_cap_a_few_long_lines_are_cut_to_it()
    {
        var text = $"{new string('x', CodeLines.MaxCharacters * 2)}\nend";

        var capped = CodeLines.Capped(text);

        capped.IsCut.Should().BeTrue();
        capped.Shown.Should().HaveLength(CodeLines.MaxCharacters);
        capped.TotalLines.Should().Be(2);
    }

    [Fact]
    public void A_cut_at_the_size_cap_never_splits_a_character_beyond_the_basic_plane()
    {
        var face = char.ConvertFromUtf32(0x1F600);
        var text = $"{new string('x', CodeLines.MaxCharacters - 1)}{face}tail";

        var capped = CodeLines.Capped(text);

        capped.IsCut.Should().BeTrue();
        capped.Shown.Should().HaveLength(CodeLines.MaxCharacters - 1, "the pair that straddles the cap is left out whole");
        char.IsHighSurrogate(capped.Shown[^1]).Should().BeFalse();
    }

    [Fact]
    public void A_character_beyond_the_basic_plane_that_ends_inside_the_cap_is_kept()
    {
        var face = char.ConvertFromUtf32(0x1F600);
        var text = $"{new string('x', CodeLines.MaxCharacters - 2)}{face}tail";

        var capped = CodeLines.Capped(text);

        capped.Shown.Should().HaveLength(CodeLines.MaxCharacters).And.EndWith(face);
    }

    [Fact]
    public void A_table_of_fewer_than_four_hundred_very_wide_rows_is_laid_out_as_capped_text()
    {
        var wide = new string('x', 2_000);
        var text = string.Concat(Enumerable.Range(1, 100).Select(row => $"{row}\t{wide}\n"));
        var table = CodeTable.Read(text)!;

        table.Rows.Count.Should().BeLessThan(CodeLines.MaxLines);
        CodeLines.TableLayoutOf(table).Should().Be(TableLayout.Text, "its cells hold more than the 64 KiB cap");
        CodeLines.Capped(text).IsCut.Should().BeTrue("as text it is cut at the same cap");
    }

    [Fact]
    public void A_table_past_four_hundred_narrow_rows_shows_its_first_rows_and_a_small_one_shows_whole()
    {
        var many = CodeTable.Read(string.Concat(Enumerable.Range(1, 500).Select(row => $"{row}\tok\n")))!;
        var few = CodeTable.Read("a\tb\n1\t2\n")!;

        CodeLines.TableLayoutOf(many).Should().Be(TableLayout.FirstRows);
        CodeLines.TableLayoutOf(few).Should().Be(TableLayout.Whole);
    }

    [Fact]
    public void A_capped_table_offers_all_its_rows_in_words() => CodeLines.ShowAllRows(1_200).Should().Be("Show all 1,200 rows");

    [Theory]
    [InlineData("a\tb", "a   b")]
    [InlineData("abcd\tb", "abcd    b")]
    [InlineData("\tx\n12\ty", "    x\n12  y")]
    [InlineData("no tabs", "no tabs")]
    public void Tabs_are_drawn_as_spaces_to_the_next_stop_of_four(string text, string drawn) => CodeLines.ExpandTabs(text).Should().Be(drawn);

    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("a\n", 1)]
    [InlineData("a\nb", 2)]
    public void Lines_are_counted_without_a_last_empty_one(string text, int count) => CodeLines.LineCount(text).Should().Be(count);

    #endregion
}
