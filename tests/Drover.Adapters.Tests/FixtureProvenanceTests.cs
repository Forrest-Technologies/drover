namespace Drover.Adapters.Tests;

/// <summary>Every recorded response says where it came from, so no hand-written file passes as a capture.</summary>
public sealed class FixtureProvenanceTests
{
    #region Provenance

    [Fact]
    public void Every_fixture_says_whether_it_was_captured_or_written_by_hand()
    {
        var fixtures = RecordedFixture.AllFixturePaths().ToList();

        fixtures.Should().NotBeEmpty();

        foreach (var path in fixtures)
        {
            var fixture = RecordedFixture.Parse(File.ReadAllText(path));

            fixture.Provenance.Should().BeOneOf("captured", "hand-written");
            fixture.Note.Should().StartWith(
                fixture.Provenance == "captured" ? "Captured" : "hand-written",
                $"{Path.GetFileName(path)} must open its note with how it was made");
        }
    }

    [Fact]
    public void Only_responses_that_were_really_captured_are_called_captured()
    {
        var captured = RecordedFixture.AllFixturePaths()
            .Where(path => RecordedFixture.Parse(File.ReadAllText(path)).Provenance == "captured")
            .Select(Path.GetFileName);

        captured.Should().BeEquivalentTo(["list_models.json", "invalid_key.json"]);
    }

    #endregion

    #region Secrets

    [Fact]
    public void No_fixture_holds_a_credential_or_a_cookie()
    {
        foreach (var path in RecordedFixture.AllFixturePaths())
        {
            var text = File.ReadAllText(path);

            text.Should().NotContainEquivalentOf("authorization", Path.GetFileName(path));
            text.Should().NotContainEquivalentOf("set-cookie", Path.GetFileName(path));
            text.Should().NotMatchRegex("(?i)bearer [a-z0-9]", Path.GetFileName(path));
        }
    }

    #endregion
}
