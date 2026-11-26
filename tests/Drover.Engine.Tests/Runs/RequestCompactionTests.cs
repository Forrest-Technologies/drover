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

        var compact = RequestCompaction.Fit(request, "sample", 300, 50, 1, 1);

        compact.Messages.Should().Contain(request.Messages[0]).And.Contain(request.Messages[3]);
        compact.Messages.SelectMany(message => message.Contents).OfType<ModelToolResult>().Single().Result.GetString().Should().Contain("left out");
        compact.Options.Should().BeSameAs(options);
        request.Messages.Should().HaveCount(4);
    }

    [Fact]
    public void A_fitting_request_passes_unchanged_and_an_indivisible_oversize_request_explains_the_limit()
    {
        var request = new ModelChatRequest([new("user", [new ModelText(new string('x', 500))])], new());
        RequestCompaction.Fit(request, "sample", 1000, 50, 1, 1).Should().BeSameAs(request);
        var fit = () => RequestCompaction.Fit(request, "sample", 100, 50, 1, 1);
        fit.Should().Throw<ModelProviderException>().WithMessage("*Send less at once*");
    }

    [Fact]
    public void Dropped_tool_groups_keep_calls_and_results_paired_for_different_recent_windows()
    {
        for (var keep = 1; keep <= 9; keep++)
        {
            List<ModelChatMessage> messages = [new("user", [new ModelText("Goal")])];
            foreach (var id in Enumerable.Range(1, 10).Select(number => number.ToString(CultureInfo.InvariantCulture)))
            {
                messages.Add(new("assistant", [new ModelToolCall(id, "lookup", JsonSerializer.SerializeToElement(new string('x', 500)))]));
                messages.Add(new("tool", [new ModelToolResult(id, JsonSerializer.SerializeToElement(id))]));
            }

            var original = new ModelChatRequest(messages, new());
            var fit = RequestCompaction.Fit(original, "sample", 1400, 100, keep, 1);
            var contents = fit.Messages.SelectMany(message => message.Contents).ToList();
            contents.OfType<ModelToolCall>().Select(call => call.CallId).Should().Equal(contents.OfType<ModelToolResult>().Select(result => result.CallId));
            fit.Messages.TakeLast(keep).Should().Equal(messages.TakeLast(keep));
        }
    }

    [Fact]
    public void Token_estimates_include_tools_and_images_and_calibration_stays_bounded()
    {
        var request = new ModelChatRequest([new("user", [new ModelText("abc")])], new());
        TokenEstimate.Request(request).Should().Be(5);
        var image = request with { Messages = [new("user", [new ModelText("abcd"), new ModelData(new byte[1], "image/png")])] };
        TokenEstimate.Request(image).Should().Be(1606);
        var tools = image with { Options = new ModelChatOptions { Tools = [new ModelTool("lookup", "description", JsonSerializer.SerializeToElement(new { }))] } };
        TokenEstimate.Request(tools).Should().BeGreaterThan(TokenEstimate.Request(image));
        TokenEstimate.Calibration(1, 100).Should().Be(0.5);
        TokenEstimate.Calibration(100, 1).Should().Be(2);
    }

    #endregion
}
