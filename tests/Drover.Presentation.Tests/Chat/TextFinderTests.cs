namespace Drover.Presentation.Tests.Chat;

/// <summary>Table tests for how a query matches, and counted work so the cost of a long message is known without
/// timing a machine.</summary>
public sealed class TextFinderTests
{
    #region Public Methods

    [Theory]
    [InlineData("run", "run running rerun", false, false, 3)]
    [InlineData("RUN", "run Running", false, false, 2)]
    [InlineData("run", "run Running", true, false, 1)]
    [InlineData("run", "run running", false, true, 1)]
    [InlineData("run", "run_id", false, true, 0)]
    [InlineData("run", "run_id running", false, false, 2)]
    [InlineData("語", "日本語の語", false, false, 2)]
    [InlineData("Cafe\u0301", "Cafe\u0301 and Cafe", false, false, 1)]
    [InlineData("bold", "bold plain bold", false, true, 2)]
    public void Matching_follows_the_options(string query, string text, bool matchCase, bool wholeWords, int expected)
    {
        var finder = new TextFinder();
        var segments = new[] { new FindSegment("m", new FindPart("Markdown"), text) };

        var matches = finder.Find(query, segments, new FindOptions(matchCase, wholeWords));

        matches.Should().HaveCount(expected);
    }

    [Fact]
    public void A_match_reports_its_position_and_piece()
    {
        var finder = new TextFinder();
        var segments = new[] { new FindSegment("m7", new FindPart("ToolSummary", 2), "open the file") };

        var match = finder.Find("file", segments).Should().ContainSingle().Subject;

        match.Should().Be(new FindMatch("m7", new FindPart("ToolSummary", 2), 9, 4));
        finder.CharactersExamined.Should().BeGreaterThan(0);
    }

    [Fact]
    public void An_empty_query_does_no_work()
    {
        var finder = new TextFinder();
        var segments = new[] { new FindSegment("m", new FindPart("Said"), "words") };

        finder.Find(string.Empty, segments).Should().BeEmpty();
        finder.CharactersExamined.Should().Be(0);
    }

    [Fact]
    public void Work_is_bounded_by_a_multiple_of_the_text_size()
    {
        var text = string.Concat(Enumerable.Repeat("a long message with words. ", 400));
        var finder = new TextFinder();
        var segments = new[] { new FindSegment("m", new FindPart("Markdown"), text) };

        var matches = finder.Find("words", segments);

        matches.Should().HaveCount(400);
        finder.CharactersExamined.Should().BeLessThanOrEqualTo(text.Length + (matches.Count * 5) + 1);
    }

    [Fact]
    public void A_small_budget_stops_the_search_early()
    {
        var text = string.Concat(Enumerable.Repeat("words ", 1000));
        var finder = new TextFinder();
        var segments = new[] { new FindSegment("m", new FindPart("Markdown"), text) };

        var matches = finder.Find("words", segments, default, budget: 100);

        matches.Should().NotBeEmpty().And.HaveCountLessThan(1000);
        finder.CharactersExamined.Should().BeLessThan(200);
    }

    #endregion
}
