namespace Drover.Engine.Tests.Sandboxes;

/// <summary>The wait a provider asked for, rounded into words a person can act on.</summary>
public sealed class SandboxWaitsTests
{
    #region Waiting

    [Fact]
    public void No_wait_says_a_moment() =>
        SandboxWaits.Words(null).Should().Be("in a moment");

    [Theory]
    [InlineData(0, "in a moment")]
    [InlineData(1, "in about 5 seconds")]
    [InlineData(29, "in about 30 seconds")]
    [InlineData(30, "in about 30 seconds")]
    [InlineData(31, "in about 35 seconds")]
    [InlineData(90, "in about 90 seconds")]
    [InlineData(91, "in about 2 minutes")]
    [InlineData(600, "in about 10 minutes")]
    [InlineData(3600, "in about 60 minutes")]
    [InlineData(3601, "later")]
    public void Waits_are_rounded_up_to_the_next_useful_word(int seconds, string expected) =>
        SandboxWaits.Words(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    #endregion
}
