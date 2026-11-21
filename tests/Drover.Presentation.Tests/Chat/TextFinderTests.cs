namespace Drover.Presentation.Tests.Chat;

/// <summary>Table tests for how a query matches, whole words across scripts, counted work, and resuming a search in
/// slices.</summary>
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
    [InlineData("Cafe", "Cafe\u0301", false, true, 0)]
    [InlineData("Cafe\u0301", "Cafe\u0301 next", false, true, 1)]
    [InlineData("x", "x\U0001F600", false, true, 1)]
    [InlineData("x", "x😀", false, false, 1)]
    public void Matching_follows_the_options(string query, string text, bool matchCase, bool wholeWords, int expected)
    {
        var finder = new TextFinder();
        FindSegment[] segments = [new("m", new FindPart("Markdown"), text)];

        var matches = finder.Find(query, segments, new FindOptions(matchCase, wholeWords)).Matches;

        matches.Should().HaveCount(expected);
    }

    [Fact]
    public void A_match_reports_its_position_and_piece()
    {
        var finder = new TextFinder();
        FindSegment[] segments = [new("m7", new FindPart("ToolSummary", 2), "open the file")];

        var match = finder.Find("file", segments).Matches.Should().ContainSingle().Subject;

        match.Should().Be(new FindMatch("m7", new FindPart("ToolSummary", 2), 9, 4));
        finder.CharactersExamined.Should().BeGreaterThan(0);
    }

    [Fact]
    public void An_empty_query_does_no_work()
    {
        var finder = new TextFinder();
        FindSegment[] segments = [new("m", new FindPart("Said"), "words")];

        var result = finder.Find(string.Empty, segments);

        result.Matches.Should().BeEmpty();
        result.Finished.Should().BeTrue();
        finder.CharactersExamined.Should().Be(0);
    }

    [Fact]
    public void Work_is_bounded_by_a_multiple_of_the_text_size()
    {
        var text = string.Concat(Enumerable.Repeat("a long message with words. ", 400));
        var finder = new TextFinder();
        FindSegment[] segments = [new("m", new FindPart("Markdown"), text)];

        var result = finder.Find("words", segments);

        result.Matches.Should().HaveCount(400);
        finder.CharactersExamined.Should().BeLessThanOrEqualTo(text.Length + (result.Matches.Count * 5) + 1);
    }

    [Fact]
    public void Slices_resume_from_the_returned_position_and_find_everything()
    {
        var text = string.Concat(Enumerable.Repeat("words ", 1000));
        var finder = new TextFinder();
        FindSegment[] segments = [new("m", new FindPart("Markdown"), text)];
        var all = finder.Find("words", segments).Matches;
        List<FindMatch> sliced = [];
        FindCursor? cursor = null;
        var slices = 0;

        while (true)
        {
            var result = finder.Find("words", segments, default, budget: 100, start: cursor);
            sliced.AddRange(result.Matches);
            slices++;

            if (result.Finished)
            {
                break;
            }

            cursor = result.Next;
            slices.Should().BeLessThan(2000, "the search must make progress");
        }

        slices.Should().BeGreaterThan(1);
        sliced.Count.Should().Be(all.Count, $"slices {slices}, sliced {sliced.Count}, all {all.Count}");
        sliced.Should().Equal(all);
    }

    [Fact]
    public void A_small_budget_stops_the_search_early_and_says_so()
    {
        var text = string.Concat(Enumerable.Repeat("words ", 1000));
        var finder = new TextFinder();
        FindSegment[] segments = [new("m", new FindPart("Markdown"), text)];

        var result = finder.Find("words", segments, default, budget: 100);

        result.Finished.Should().BeFalse();
        result.Matches.Should().NotBeEmpty().And.HaveCountLessThan(1000);
        result.Next.Offset.Should().BeGreaterThan(0);
        finder.CharactersExamined.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void The_finder_works_on_the_segments_the_walker_builds()
    {
        var findable = new FindableText();
        var segments = findable.Of(new ChatMessage("m", false, "**bold**ly"));
        var finder = new TextFinder();

        finder.Find("oldl", segments).Matches.Should().Contain(match => match.Start == 1 && match.Length == 4);
        finder.Find("boldly", segments).Matches.Should().Contain(match => match.Start == 0 && match.Length == 6);
    }

    #endregion
}
