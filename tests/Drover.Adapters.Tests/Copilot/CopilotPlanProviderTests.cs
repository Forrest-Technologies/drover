namespace Drover.Adapters.Tests;

public sealed class CopilotPlanProviderTests
{
    #region Listing

    [Fact]
    public async Task A_failed_model_list_is_reported_in_plain_words()
    {
        var provider = Provider(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var listing = () => provider.ListModels(
            new ModelEndpoint(ModelConnectionKinds.CopilotSignIn, provider.FixedAddress!),
            "session-token",
            TestContext.Current.CancellationToken);

        var failure = await listing.Should().ThrowAsync<ModelProviderException>();
        failure.Which.Failure.Should().Be(ModelFailure.Unreachable);
        failure.Which.Message.Should().Contain("HTTP 502");
    }

    [Fact]
    public async Task An_empty_catalog_is_returned_as_empty()
    {
        var provider = Provider(_ => Task.FromResult(ScriptedHandler.Json(HttpStatusCode.OK, """{"data":[]}""")));

        var models = await provider.ListModels(
            new ModelEndpoint(ModelConnectionKinds.CopilotSignIn, provider.FixedAddress!),
            "session-token",
            TestContext.Current.CancellationToken);

        models.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_list_fails_the_connection_test_with_the_same_words()
    {
        var provider = Provider(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));

        var result = await provider.TestConnection(
            new ModelEndpoint(ModelConnectionKinds.CopilotSignIn, provider.FixedAddress!),
            "session-token",
            TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(ModelFailure.Unreachable);
        result.Message.Should().Contain("HTTP 502");
    }

    #endregion

    #region Private Methods

    private static CopilotPlanProvider Provider(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new(
            new FixtureHttpClientFactory(new ScriptedHandler((request, _, _) => respond(request))),
            new SettableOptions<CopilotPlanOptions>(new CopilotPlanOptions()));

    #endregion
}
