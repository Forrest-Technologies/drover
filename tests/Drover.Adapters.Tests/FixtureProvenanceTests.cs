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
            .Select(path => $"{Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileName(path)}");

        captured.Should().BeEquivalentTo(
        [
            "venice/list_models.json",
            "venice/invalid_key.json",
            "e2b/creates_with_metadata.json",
            "e2b/keeps_alive.json",
            "e2b/runs_a_command.json",
            "e2b/exits_zero.json",
            "e2b/round_trips_binary.json",
            "e2b/lists_files.json",
            "e2b/denies_network.json",
            "e2b/pauses_and_resumes.json",
            "e2b/destroys.json",
            "e2b/refuses_another_groups_handle.json",
            "e2b/refuses_a_wrong_token.json",
            "e2b/kills_on_cancel.json",
            "e2b/stops_at_the_time_limit.json",
            "e2b/malformed_key.json",
        ]);
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
