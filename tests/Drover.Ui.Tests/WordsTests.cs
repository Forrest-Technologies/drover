namespace Drover.Ui.Tests;

/// <summary>The small pieces of wording every screen shares. Moved here from the starters with the first one that used them.</summary>
public sealed class WordsTests
{
    #region Private Fields

    private static readonly int[] ordinals = [1, 2, 3, 4, 11, 12, 13, 21, 22, 23, 31];

    #endregion

    #region Tests

    [Fact]
    public void Counts_lists_ordinals_sizes_and_durations_read_the_same_everywhere()
    {
        Words.Count(1, "run").Should().Be("1 run");
        Words.Count(1200, "run").Should().Be("1,200 runs");
        Words.List([]).Should().BeEmpty();
        Words.List(["a"]).Should().Be("a");
        Words.List(["a", "b"]).Should().Be("a and b");
        Words.List(["a", "b", "c"]).Should().Be("a, b and c");
        ordinals.Select(Words.Ordinal).Should().Equal(
            "1st", "2nd", "3rd", "4th", "11th", "12th", "13th", "21st", "22nd", "23rd", "31st");
        Words.Size(840).Should().Be("840 bytes");
        Words.Size(12_288).Should().Be("12 KB");
        Words.Size(3_565_158).Should().Be("3.4 MB");
        Words.Duration(TimeSpan.FromMilliseconds(300)).Should().Be("under a second");
        Words.Duration(TimeSpan.FromSeconds(12)).Should().Be("12 seconds");
        Words.Duration(TimeSpan.FromMinutes(3)).Should().Be("3 minutes");
        Words.Duration(new TimeSpan(2, 5, 0)).Should().Be("2 hours 5 minutes");
        Words.Clock(TimeSpan.FromSeconds(67)).Should().Be("1:07");
    }

    #endregion
}
