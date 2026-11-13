namespace Drover.Engine.Tests;

public sealed class CatalogUpsertTests
{
    #region Private Fields

    private static readonly Guid connection = Guid.CreateVersion7();

    private static readonly DateTimeOffset now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    #endregion

    #region Public Methods

    [Fact]
    public void New_models_are_added_in_drovers_own_shape()
    {
        var info = new ModelInfo("alpha", "Alpha", new ModelCapabilities { Vision = true }, ["default"])
        {
            Pricing = new ModelPricing { InputPerMillion = 1m },
            Privacy = PrivacyMode.Anonymized,
        };

        var added = CatalogUpsert.Apply(connection, [], [info], now);

        var entry = added.Should().ContainSingle().Subject;
        entry.ModelConnectionId.Should().Be(connection);
        entry.Capabilities.Vision.Should().BeTrue();
        entry.Pricing.InputPerMillion.Should().Be(1m);
        entry.PrivacyMode.Should().Be("anonymized");
        entry.Traits.Should().Equal("default");
        entry.State.Should().Be(AvailabilityState.Available);
        entry.LastSeenAt.Should().Be(now);
    }

    [Fact]
    public void A_listed_model_is_updated_in_place_and_comes_back_when_it_was_missing()
    {
        var existing = new ModelCatalogEntry { Model = "alpha", DisplayName = "Old", State = AvailabilityState.Missing };

        var added = CatalogUpsert.Apply(connection, [existing], [new ModelInfo("alpha", "New", new ModelCapabilities(), [])], now);

        added.Should().BeEmpty();
        existing.DisplayName.Should().Be("New");
        existing.State.Should().Be(AvailabilityState.Available);
        existing.PrivacyMode.Should().BeNull("an unknown privacy mode is stored as unknown, not as a word");
    }

    [Fact]
    public void A_model_no_longer_listed_keeps_its_row_marked_missing()
    {
        var gone = new ModelCatalogEntry { Model = "gone", DisplayName = "Gone", LastSeenAt = now.AddDays(-1) };

        CatalogUpsert.Apply(connection, [gone], [new ModelInfo("kept", "Kept", new ModelCapabilities(), [])], now);

        gone.State.Should().Be(AvailabilityState.Missing);
        gone.LastSeenAt.Should().Be(now.AddDays(-1));
    }

    [Fact]
    public void An_empty_list_marks_nothing_missing_because_a_server_without_a_list_looks_the_same()
    {
        var kept = new ModelCatalogEntry { Model = "kept", DisplayName = "Kept" };

        CatalogUpsert.Apply(connection, [kept], [], now);

        kept.State.Should().Be(AvailabilityState.Available);
    }

    [Fact]
    public void Duplicate_and_overlong_ids_are_skipped_rather_than_failing_the_refresh()
    {
        ModelInfo[] listed =
        [
            new("dup", "First", new ModelCapabilities(), []),
            new("dup", "Second", new ModelCapabilities(), []),
            new(new string('x', CatalogUpsert.MaxModelIdLength + 1), "Too long", new ModelCapabilities(), []),
        ];

        var added = CatalogUpsert.Apply(connection, [], listed, now);

        added.Should().ContainSingle().Which.DisplayName.Should().Be("First");
    }

    #endregion
}
