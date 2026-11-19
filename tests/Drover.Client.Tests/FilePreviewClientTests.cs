namespace Drover.Client.Tests;

/// <summary>The typed client's mint and revoke calls for one isolated HTML preview.</summary>
public sealed class FilePreviewClientTests
{
    #region Public Methods

    [Fact]
    public async Task A_preview_is_minted_and_its_grant_is_revoked_on_the_file_route()
    {
        var spaceId = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();
        var file = $"https://drover.test/api/spaces/{spaceId}/files/{fileId}";
        var server = new RecordingHandler();
        var api = new DroverApi(Http(server)) { AntiforgeryToken = "antiforgery" };

        var preview = await api.PreviewConversationFile(file, TestContext.Current.CancellationToken);
        await api.RevokeConversationFilePreview(file, preview.Grant!, TestContext.Current.CancellationToken);

        preview.Should().Be(new FilePreviewResponse("https://preview.test/preview/file?grant=secret", Grant: "secret"));
        server.Requests.Select(request => (request.Method, request.Path)).Should().Equal(
            (HttpMethod.Post, $"/api/spaces/{spaceId}/files/{fileId}/preview"),
            (HttpMethod.Post, $"/api/spaces/{spaceId}/files/{fileId}/preview/revoke"));
        server.Requests.Should().OnlyContain(request => request.Antiforgery == "antiforgery");
        server.Requests[^1].Body.Should().Be("{\"grant\":\"secret\"}");
    }

    [Fact]
    public async Task A_preview_is_not_requested_for_a_file_outside_the_configured_server()
    {
        var server = new RecordingHandler();
        var api = new DroverApi(Http(server));

        var action = () => api.PreviewConversationFile("https://outside.test/api/spaces/1/files/2");

        await action.Should().ThrowAsync<ArgumentException>();
        server.Requests.Should().BeEmpty();
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

        public List<RecordedRequest> Requests { get; } = [];

        #endregion

        #region Protected Methods

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(
                request.Method,
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.TryGetValues("X-XSRF-TOKEN", out var tokens) ? tokens.Single() : null,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            return request.RequestUri?.AbsolutePath.EndsWith("/revoke", StringComparison.Ordinal) is true
                ? new(HttpStatusCode.NoContent)
                : new(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new FilePreviewResponse(
                        "https://preview.test/preview/file?grant=secret",
                        Grant: "secret")),
                };
        }

        #endregion
    }

    private sealed record RecordedRequest(HttpMethod Method, string Path, string? Antiforgery, string? Body);

    #endregion
}
