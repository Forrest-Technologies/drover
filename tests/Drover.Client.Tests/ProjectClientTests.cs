namespace Drover.Client.Tests;

/// <summary>The typed project calls, including their paths, bodies, responses, and refusals.</summary>
public sealed class ProjectClientTests
{
    #region Public Methods

    [Fact]
    public async Task Project_calls_use_the_space_routes_and_read_their_contracts()
    {
        var spaceId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        var conversationId = Guid.CreateVersion7();
        var project = new ProjectResponse(projectId, "Launch", "Use approved facts.", [conversationId]);
        List<ProjectResponse> projectList = [project];
        var server = new RecordingHandler(
            Json(project),
            Json(projectList),
            Json(project with { Name = "Release" }),
            Empty(),
            Empty());
        var api = new DroverApi(Http(server)) { AntiforgeryToken = "token" };

        var created = await api.CreateProject(spaceId, new ProjectBody("Launch", "Use approved facts."), Token);
        var listed = await api.ListProjects(spaceId, Token);
        var updated = await api.UpdateProject(spaceId, projectId, new ProjectBody("Release", "Use approved facts."), Token);
        await api.SetConversationProject(spaceId, conversationId, projectId, Token);
        await api.RemoveProject(spaceId, projectId, Token);

        created.Should().BeEquivalentTo(project);
        listed.Should().BeEquivalentTo([project]);
        updated.Name.Should().Be("Release");
        server.Requests.Select(request => (request.Method, request.Path)).Should().Equal(
            (HttpMethod.Post, $"/api/spaces/{spaceId}/projects"),
            (HttpMethod.Get, $"/api/spaces/{spaceId}/projects"),
            (HttpMethod.Put, $"/api/spaces/{spaceId}/projects/{projectId}"),
            (HttpMethod.Put, $"/api/spaces/{spaceId}/threads/{conversationId}/project"),
            (HttpMethod.Delete, $"/api/spaces/{spaceId}/projects/{projectId}"));
        server.Requests.Where(request => request.Method != HttpMethod.Get)
            .Should().OnlyContain(request => request.Token == "token");
        server.Requests[3].Body.Should().Contain($"\"projectId\":\"{projectId}\"");
    }

    [Fact]
    public async Task A_project_refusal_throws_with_the_servers_status_and_words()
    {
        var server = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { title = "Give the project a name.", status = 400 }),
        });
        var api = new DroverApi(Http(server)) { AntiforgeryToken = "token" };

        var create = () => api.CreateProject(Guid.CreateVersion7(), new ProjectBody("", ""), Token);

        var thrown = await create.Should().ThrowAsync<DroverApiException>();
        thrown.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        thrown.Which.Message.Should().Contain("Give the project a name");
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static HttpResponseMessage Empty() => new(HttpStatusCode.NoContent);

    // The banned constructor guards outbound calls in the product. This client reaches only the recording handler.
#pragma warning disable RS0030
    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://drover.test/") };
#pragma warning restore RS0030

    #endregion

    #region Nested Types

    private sealed record RequestFact(HttpMethod Method, string Path, string? Token, string? Body);

    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        #region Private Fields

        private readonly Queue<HttpResponseMessage> remaining = new(responses);

        #endregion

        #region Properties

        public List<RequestFact> Requests { get; } = [];

        #endregion

        #region Protected Methods

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("X-XSRF-TOKEN", out var values) ? values.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return remaining.Dequeue();
        }

        #endregion
    }

    #endregion
}
