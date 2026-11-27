namespace Drover.Adapters.Tests;

/// <summary>
/// How the provider's failures reach Drover: in its terms, with the wait the provider named, never with the key, the
/// provider's own words, or the vendor's exception. The error bodies are hand-written from
/// https://platform.claude.com/docs/en/api/errors and https://platform.claude.com/docs/en/api/$shared (read 2026-11-27).
/// </summary>
public sealed class AnthropicFailureTests
{
    #region Provider Failures

    [Theory]
    [InlineData("overloaded", ModelFailure.RetryLater)]
    [InlineData("credit_too_low", ModelFailure.OutOfCredit)]
    [InlineData("spend_limit", ModelFailure.OutOfCredit)]
    [InlineData("context_length_exceeded", ModelFailure.TooLong)]
    [InlineData("rate_limited", ModelFailure.RetryLater)]
    [InlineData("invalid_key", ModelFailure.InvalidKey)]
    public async Task A_provider_failure_on_a_run_is_drovers_own(string fixture, ModelFailure expected)
    {
        var subject = AnthropicRig.Start(fixture).Subject;

        var failure = (await FluentActions.Awaiting(() => subject.CreateChatClient().GetResponse(Prompt(), Token))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(expected);
        AssertClean(failure, subject.ApiKey);
    }

    [Fact]
    public async Task A_rate_limit_carries_the_wait_the_provider_named_and_its_headers()
    {
        var subject = AnthropicRig.Start("rate_limited").Subject;

        var failure = (await FluentActions.Awaiting(() => subject.CreateChatClient().GetResponse(Prompt(), Token))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(12));
        failure.ResponseHeaders.Should().ContainKey("anthropic-ratelimit-requests-remaining");
    }

    [Fact]
    public async Task An_overloaded_provider_in_the_middle_of_a_stream_means_retry_later()
    {
        var subject = AnthropicRig.Start("overloaded_in_stream").Subject;

        var failure = (await FluentActions.Awaiting(() => Drain(subject.CreateChatClient().GetStreamingResponse(Prompt(), Token)))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.RetryLater);
        AssertClean(failure, subject.ApiKey);
    }

    [Fact]
    public async Task A_wrong_key_is_an_invalid_key_on_test_and_on_listing()
    {
        var subject = AnthropicRig.Start("invalid_key").Subject;

        var test = await subject.Provider.TestConnection(subject.Connection, subject.ApiKey, Token);
        var listing = (await FluentActions.Awaiting(() => subject.Provider.ListModels(subject.Connection, subject.ApiKey, Token))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        test.Failure.Should().Be(ModelFailure.InvalidKey);
        test.Message.Should().NotContain(subject.ApiKey);
        listing.Failure.Should().Be(ModelFailure.InvalidKey);
        AssertClean(listing, subject.ApiKey);
    }

    [Fact]
    public async Task No_key_is_a_setup_message_and_nothing_is_sent()
    {
        var rig = AnthropicRig.Start("plain_reply");
        var subject = rig.Subject;

        var test = await subject.Provider.TestConnection(subject.Connection, null, Token);

        test.Should().Be(ConnectionTestResult.Failed(ModelFailure.InvalidKey, "Paste an Anthropic API key to finish setup."));
        FluentActions.Invoking(() => subject.Provider.CreateChatClient(subject.Connection, null, subject.ModelId, new ModelSettings()))
            .Should().Throw<ModelProviderException>().Which.Failure.Should().Be(ModelFailure.InvalidKey);
        rig.Handler.Requests.Should().BeEmpty();
    }

    #endregion

    #region Refused Addresses

    [Fact]
    public async Task A_private_address_refusal_is_reported_as_a_refusal()
    {
        var refusal = "This space does not allow calls to private network addresses.";
        var handler = new ScriptedHandler((_, _, _) => throw new OutboundBlockedException(refusal));
        var provider = new AnthropicProvider(new FixtureHttpClientFactory(handler));
        var connection = new ModelEndpoint(AnthropicProvider.ConnectionKind, AnthropicProvider.BaseAddress);
        var key = TestKeys.Generate();

        var test = await provider.TestConnection(connection, key, Token);
        var run = (await FluentActions.Awaiting(() => provider.CreateChatClient(connection, key, "claude-test-model", new ModelSettings()).GetResponse(Prompt(), Token))
            .Should().ThrowAsync<ModelProviderException>()).Which;

        test.Should().Be(ConnectionTestResult.Failed(ModelFailure.Rejected, refusal));
        run.Failure.Should().Be(ModelFailure.Rejected);
        run.Message.Should().Be(refusal);
        AssertClean(run, key);
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelChatRequest Prompt() =>
        new([new ModelChatMessage("user", [new ModelText(ModelProviderConformance.GreetingPrompt)])], new ModelChatOptions());

    /// <summary>The provider's words can quote the input, so neither they nor the key may leave the adapter.</summary>
    private static void AssertClean(ModelProviderException failure, string key)
    {
        failure.Message.Should().NotContain(key);
        failure.Message.Should().NotContainAny("prompt is too long", "credit balance", "Overloaded", "invalid x-api-key", "usage limits");
        failure.InnerException.Should().BeNull("the vendor's exception stays inside the adapter");
    }

    private static async Task Drain(IAsyncEnumerable<ModelChatUpdate> updates)
    {
        await foreach (var update in updates)
        {
            update.Contents.Should().NotBeNull();
        }
    }

    #endregion
}
