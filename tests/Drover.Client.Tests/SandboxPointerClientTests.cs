namespace Drover.Client.Tests;

/// <summary>
/// The typed client's calls for a pointer on a bot's sandbox screen: each asks the input route for one kind of
/// action, with the button named in words, and a drag carries its end.
/// </summary>
public sealed class SandboxPointerClientTests
{
    #region Public Methods

    [Fact]
    public async Task Each_pointer_call_posts_one_input_of_its_kind_to_the_input_route()
    {
        var spaceId = Guid.CreateVersion7();
        var botId = Guid.CreateVersion7();
        var server = new RecordingHandler();
        var api = new DroverApi(Http(server)) { AntiforgeryToken = "token" };
        var cancellationToken = TestContext.Current.CancellationToken;

        await api.PressBotSandboxButton(spaceId, botId, 10, 20, "right", cancellationToken);
        await api.MoveBotSandboxPointer(spaceId, botId, 30, 40, cancellationToken);
        await api.ReleaseBotSandboxButton(spaceId, botId, "middle", 50, 60, cancellationToken);
        await api.ReleaseBotSandboxButton(spaceId, botId, cancellationToken: cancellationToken);
        await api.DragBotSandboxPointer(spaceId, botId, 1, 2, 300, 200, "left", cancellationToken);

        server.Paths.Should().OnlyContain(path => path == $"/api/spaces/{spaceId}/bots/{botId}/sandbox/input");
        server.Methods.Should().OnlyContain(method => method == HttpMethod.Post);
        server.Bodies.Select(body => JsonSerializer.Deserialize<SandboxInputRequest>(body, JsonSerializerOptions.Web)).Should().Equal(
            new SandboxInputRequest("down", X: 10, Y: 20, Button: "right"),
            new SandboxInputRequest("move", X: 30, Y: 40),
            new SandboxInputRequest("up", X: 50, Y: 60, Button: "middle"),
            new SandboxInputRequest("up", Button: "left"),
            new SandboxInputRequest("drag", X: 1, Y: 2, Button: "left", X2: 300, Y2: 200));
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

        public List<HttpMethod> Methods { get; } = [];

        public List<string?> Paths { get; } = [];

        public List<string> Bodies { get; } = [];

        #endregion

        #region Protected Methods

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method);
            Paths.Add(request.RequestUri?.PathAndQuery);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        #endregion
    }

    #endregion
}
