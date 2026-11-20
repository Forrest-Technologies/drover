namespace Drover.Engine.Tests.Bots;

public sealed class ModelFallbackTests
{
    #region Public Methods

    [Fact]
    public void A_bot_list_wins_over_the_space_list_and_keeps_order()
    {
        var connection = Guid.NewGuid();
        var own = new[] { new ModelFallback(connection, "second"), new ModelFallback(connection, "third") };
        var space = new SpaceSettings { ModelFallbacks = [new ModelFallback(connection, "space")] };

        var resolved = ModelFallbackService.ResolveFallbacks(new ModelSettings { Fallbacks = own }, space);

        resolved.Should().Equal(own);
    }

    [Fact]
    public void An_empty_bot_list_uses_the_space_list_unless_the_bot_opts_out()
    {
        var space = new SpaceSettings { ModelFallbacks = [new ModelFallback(Guid.NewGuid(), "space")] };

        ModelFallbackService.ResolveFallbacks(new ModelSettings(), space).Should().Equal(space.ModelFallbacks);
        ModelFallbackService.ResolveFallbacks(new ModelSettings { UseSpaceFallbacks = false }, space).Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_primary_duplicate_fallback_and_sixth_entry_are_refused_in_sentences()
    {
        var connection = Guid.NewGuid();
        var primary = new ModelFallback(connection, "primary");

        ModelFallbackService.FindListProblem([primary], primary).Should().EndWith(".");
        ModelFallbackService.FindListProblem(
            [new ModelFallback(connection, "same"), new ModelFallback(connection, "same")],
            primary).Should().EndWith(".");
        ModelFallbackService.FindListProblem(
            [.. Enumerable.Range(1, 6).Select(number => new ModelFallback(connection, $"model-{number}"))],
            primary).Should().EndWith(".");
    }

    #endregion
}
