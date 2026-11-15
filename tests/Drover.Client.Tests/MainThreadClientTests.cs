namespace Drover.Client.Tests;

/// <summary>
/// The typed client's calls for a person's main thread with a bot and its running summary: what they send and what
/// they read back.
/// </summary>
public sealed class MainThreadClientTests
{
    #region Public Methods

    [Fact]
    public async Task Opening_the_main_thread_puts_to_the_bots_main_thread_with_the_antiforgery_token_and_reads_the_thread()
    {
        var spaceId = Guid.CreateVersion7();
        var botId = Guid.CreateVersion7();
        var thread = new ThreadSummary(Guid.CreateVersion7(), botId, null, false, DateTimeOffset.UnixEpoch, null, IsMain: true);
        var server = new RecordingHandler(HttpStatusCode.OK, JsonSerializer.Serialize(thread, JsonSerializerOptions.Web));
        var api = new DroverApi(Http(server)) { AntiforgeryToken = "token" };

        var opened = await api.OpenMainThread(spaceId, botId, TestContext.Current.CancellationToken);

        opened.Should().Be(thread);
        server.Method.Should().Be(HttpMethod.Put);
        server.Path.Should().Be($"/api/spaces/{spaceId}/bots/{botId}/main-thread");
        server.Token.Should().Be("token");
        server.HadBody.Should().BeFalse();
    }

    [Fact]
    public async Task Reading_the_summary_gets_the_threads_summary_and_reads_it_back()
    {
        var spaceId = Guid.CreateVersion7();
        var threadId = Guid.CreateVersion7();
        var summary = new ConversationSummary("They planned the week.", 14, DateTimeOffset.UnixEpoch);
        var server = new RecordingHandler(HttpStatusCode.OK, JsonSerializer.Serialize(summary, JsonSerializerOptions.Web));
        var api = new DroverApi(Http(server));

        var read = await api.GetConversationSummary(spaceId, threadId, TestContext.Current.CancellationToken);

        read.Should().Be(summary);
        server.Method.Should().Be(HttpMethod.Get);
        server.Path.Should().Be($"/api/spaces/{spaceId}/threads/{threadId}/summary");
        server.HadBody.Should().BeFalse();
    }

    [Fact]
    public async Task A_summary_not_written_yet_reads_back_with_no_text()
    {
        var server = new RecordingHandler(HttpStatusCode.OK, """{"text":null,"messagesCovered":0,"updatedAt":null}""");
        var api = new DroverApi(Http(server));

        var read = await api.GetConversationSummary(Guid.CreateVersion7(), Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        read.Should().Be(new ConversationSummary(null, 0, null));
    }

    [Fact]
    public async Task A_refusal_to_open_the_main_thread_throws_with_its_status()
    {
        var server = new RecordingHandler(HttpStatusCode.NotFound, """{"title":"Not Found","status":404}""");
        var api = new DroverApi(Http(server));

        var open = () => api.OpenMainThread(Guid.CreateVersion7(), Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        var thrown = await open.Should().ThrowAsync<DroverApiException>();
        thrown.Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
        thrown.Which.Message.Should().Be("Not Found");
    }

    #endregion

    #region Private Methods

    // The banned constructor guards outbound calls in the product. This client reaches only the recording handler.
#pragma warning disable RS0030
    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://drover.test/") };
#pragma warning restore RS0030

    #endregion

    #region Nested Types

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        #region Properties

        public HttpMethod? Method { get; private set; }

        public string? Path { get; private set; }

        public string? Token { get; private set; }

        public bool HadBody { get; private set; }

        #endregion

        #region Protected Methods

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Path = request.RequestUri?.AbsolutePath;
            Token = request.Headers.TryGetValues("X-XSRF-TOKEN", out var values) ? values.Single() : null;
            HadBody = request.Content is not null;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        #endregion
    }

    #endregion
}
