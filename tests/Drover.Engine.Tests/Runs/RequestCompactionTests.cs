namespace Drover.Engine.Tests.Runs;

public sealed class RequestCompactionTests
{
    #region Public Methods

    [Fact]
    public void Oversized_history_loses_a_tool_exchange_together_and_keeps_the_original_transcript()
    {
        var options = new ModelChatOptions { Instructions = "Follow the rules.", MaxOutputTokens = 50 };
        var request = new ModelChatRequest([
            new("user", [new ModelText("Original task")]),
            new("assistant", [new ModelToolCall("one", "lookup", JsonSerializer.SerializeToElement(new { }))]),
            new("tool", [new ModelToolResult("one", JsonSerializer.SerializeToElement(new string('x', 4000)))]),
            new("user", [new ModelText("Continue")]),
        ], options);

        var compact = RequestCompaction.Fit(request, new ModelCapabilities { ContextTokens = 300 }, new MemorySettings());

        compact.Messages.Should().Equal(request.Messages[0], request.Messages[3]);
        compact.Options.Should().BeSameAs(options);
        request.Messages.Should().HaveCount(4);
    }

    [Fact]
    public void A_fitting_request_passes_unchanged_and_an_indivisible_oversize_request_explains_the_limit()
    {
        var request = new ModelChatRequest([new("user", [new ModelText(new string('x', 500))])], new());
        RequestCompaction.Fit(request, null, new MemorySettings()).Should().BeSameAs(request);
        var fit = () => RequestCompaction.Fit(request, new ModelCapabilities { ContextTokens = 100 }, new MemorySettings());
        fit.Should().Throw<ModelProviderException>().WithMessage("*smaller message*");
    }

    #endregion
}
