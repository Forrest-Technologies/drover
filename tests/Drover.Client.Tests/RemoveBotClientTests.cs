namespace Drover.Client.Tests;

/// <summary>
/// The typed client's call that removes a bot: what it asks the server to do with the bots beneath it. Leaving the
/// choice out sends none, so the server, not the client, decides whether one is needed.
/// </summary>
public sealed class RemoveBotClientTests
{
    #region Public Methods

    [Theory]
    [InlineData(null, "")]
    [InlineData(true, "?beneath=remove")]
    [InlineData(false, "?beneath=keep")]
    public async Task Removing_a_bot_deletes_it_and_says_what_happens_to_the_bots_beneath_only_when_told(bool? removeBeneath, string query)
    {
        var spaceId = Guid.CreateVersion7();
        var botId = Guid.CreateVersion7();
        var server = new RecordingHandler();
        var api = new DroverApi(Http(server)) { AntiforgeryToken = "token" };

        await api.RemoveBot(spaceId, botId, removeBeneath, TestContext.Current.CancellationToken);

        server.Method.Should().Be(HttpMethod.Delete);
        server.PathAndQuery.Should().Be($"/api/spaces/{spaceId}/bots/{botId}{query}");
    }

    #endregion

    #region Private Methods

    // The banned constructor guards outbound calls in the product. This client reaches only the recording handler.
#pragma warning disable RS0030
    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://drover.test/") };
#pragma warning restore RS0030

    #endregion

    #region Nested Types

    private sealed class RecordingHandler : HttpMessageHandler
    {
        #region Properties

        public HttpMethod? Method { get; private set; }

        public string? PathAndQuery { get; private set; }

        #endregion

        #region Protected Methods

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        #endregion
    }

    #endregion
}
