namespace Drover.Client.Tests;

/// <summary>The typed client's calls for projects and conversation project membership.</summary>
public sealed class ProjectClientTests
{
    #region Public Methods

    [Fact]
    public async Task Project_calls_hit_the_expected_routes_and_read_contracts_back()
    {
        var spaceId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
        var botId = Guid.CreateVersion7();
        var threadId = Guid.CreateVersion7();
        var expectedProject = new ProjectSummary(projectId, "Launch", "Be concise.", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var expectedThread = new ThreadSummary(threadId, botId, "Plan", false, DateTimeOffset.UnixEpoch, null, false, null, projectId);
        var server = new RecordingHandler();
        var api = new DroverApi(Http(server)) { AntiforgeryToken = "xsrf" };

        server.Respond(HttpStatusCode.Created, expectedProject);
        var created = await api.CreateProject(spaceId, new CreateProjectRequest("Launch", "Be concise."), TestContext.Current.CancellationToken);
        created.Should().Be(expectedProject);
        server.LastRequest.Should().Be((HttpMethod.Post, $"/api/spaces/{spaceId}/projects", "xsrf", true));

        server.Respond(HttpStatusCode.OK, new List<ProjectSummary> { expectedProject });
        (await api.ListProjects(spaceId, TestContext.Current.CancellationToken)).Should().Equal([expectedProject]);
        server.LastRequest.Should().Be((HttpMethod.Get, $"/api/spaces/{spaceId}/projects", null, false));

        server.Respond(HttpStatusCode.OK, expectedProject);
        (await api.GetProject(spaceId, projectId, TestContext.Current.CancellationToken)).Should().Be(expectedProject);
        server.LastRequest.Should().Be((HttpMethod.Get, $"/api/spaces/{spaceId}/projects/{projectId}", null, false));

        server.Respond(HttpStatusCode.OK, expectedProject);
        (await api.UpdateProject(spaceId, projectId, new UpdateProjectRequest("Launch v2"), TestContext.Current.CancellationToken)).Should().Be(expectedProject);
        server.LastRequest.Should().Be((HttpMethod.Put, $"/api/spaces/{spaceId}/projects/{projectId}", "xsrf", true));

        server.Respond(HttpStatusCode.OK, new List<ThreadSummary> { expectedThread });
        (await api.ListProjectThreads(spaceId, projectId, TestContext.Current.CancellationToken)).Should().Equal([expectedThread]);
        server.LastRequest.Should().Be((HttpMethod.Get, $"/api/spaces/{spaceId}/projects/{projectId}/threads", null, false));

        server.Respond(HttpStatusCode.Created, expectedThread);
        (await api.CreateThread(spaceId, botId, "Plan", projectId, TestContext.Current.CancellationToken)).Should().Be(expectedThread);
        server.LastRequest.Should().Be((HttpMethod.Post, $"/api/spaces/{spaceId}/bots/{botId}/threads", "xsrf", true));
        server.LastBody.Should().Contain(projectId.ToString());

        server.Respond(HttpStatusCode.OK, expectedThread);
        (await api.SetThreadProject(spaceId, threadId, projectId, TestContext.Current.CancellationToken)).Should().Be(expectedThread);
        server.LastRequest.Should().Be((HttpMethod.Put, $"/api/spaces/{spaceId}/threads/{threadId}/project", "xsrf", true));

        server.Respond(HttpStatusCode.NoContent, string.Empty);
        await api.RemoveProject(spaceId, projectId, TestContext.Current.CancellationToken);
        server.LastRequest.Should().Be((HttpMethod.Delete, $"/api/spaces/{spaceId}/projects/{projectId}", "xsrf", false));

        var fileId = Guid.CreateVersion7();
        server.Respond(HttpStatusCode.OK, new FileRules(10_485_760));
        (await api.GetFileRules(spaceId, TestContext.Current.CancellationToken)).Should().Be(new FileRules(10_485_760));
        server.LastRequest.Should().Be((HttpMethod.Get, $"/api/spaces/{spaceId}/files/rules", null, false));

        server.Respond(HttpStatusCode.OK, new List<ThreadSummary> { expectedThread });
        (await api.ListOwnedThreads(spaceId, TestContext.Current.CancellationToken)).Should().Equal([expectedThread]);
        server.LastRequest.Should().Be((HttpMethod.Get, $"/api/spaces/{spaceId}/threads", null, false));

        server.Respond(HttpStatusCode.OK, "hello");
        using var downloaded = await api.OpenConversationFile(
            api.ProjectFileAddress(spaceId, projectId, fileId, download: true),
            TestContext.Current.CancellationToken);
        downloaded.StatusCode.Should().Be(HttpStatusCode.OK);
        server.LastRequest.Should().Be((HttpMethod.Get, $"/api/spaces/{spaceId}/projects/{projectId}/files/{fileId}", null, false));

        var outsideOrigin = () => api.OpenConversationFile(
            $"https://other.example.test/api/spaces/{spaceId}/projects/{projectId}/files/{fileId}",
            TestContext.Current.CancellationToken);
        await outsideOrigin.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Give the project a name.")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData(HttpStatusCode.NotFound, "Not Found")]
    public async Task Refused_project_calls_throw_DroverApiException_with_status_and_title(HttpStatusCode status, string title)
    {
        var server = new RecordingHandler();
        server.RespondRaw(status, JsonSerializer.Serialize(new { title, status = (int)status }));
        var api = new DroverApi(Http(server)) { AntiforgeryToken = "xsrf" };

        var act = () => api.SetThreadProject(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        var thrown = await act.Should().ThrowAsync<DroverApiException>();
        thrown.Which.StatusCode.Should().Be(status);
        thrown.Which.Message.Should().Be(title);
    }

    #endregion

    #region Private Methods

#pragma warning disable RS0030
    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://drover.test/") };
#pragma warning restore RS0030

    #endregion

    #region Nested Types

    private sealed class RecordingHandler : HttpMessageHandler
    {
        #region Private Fields

        private HttpStatusCode nextStatus = HttpStatusCode.OK;
        private string nextBody = "{}";

        #endregion

        #region Properties

        public (HttpMethod Method, string Path, string? Token, bool HadBody) LastRequest { get; private set; }

        public string LastBody { get; private set; } = string.Empty;

        #endregion

        #region Public Methods

        public void Respond<T>(HttpStatusCode status, T value) =>
            RespondRaw(status, JsonSerializer.Serialize(value, JsonSerializerOptions.Web));

        public void RespondRaw(HttpStatusCode status, string json)
        {
            nextStatus = status;
            nextBody = json;
        }

        #endregion

        #region Protected Methods

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = request.Content is not null
                ? await request.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;
            LastRequest = (
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.TryGetValues("X-XSRF-TOKEN", out var values) ? values.Single() : null,
                request.Content is not null);
            return new HttpResponseMessage(nextStatus)
            {
                Content = new StringContent(nextBody, Encoding.UTF8, "application/json"),
            };
        }

        #endregion
    }

    #endregion
}
