namespace Drover.Client.Tests;

public sealed class FileUploadClientTests
{
    #region Public Methods

    [Fact]
    public async Task The_content_length_is_known_before_the_handler_reads_an_unmeasured_file()
    {
        var token = TestContext.Current.CancellationToken;
        var bytes = "the file's bytes"u8.ToArray();
        var file = new FileResponse(Guid.NewGuid(), "notes.txt", "text/plain", "text", bytes.Length);
        using var stream = new MemoryStream(bytes);
        using var http = Http(async (request, ct) =>
        {
            var content = request.Content!;
            content.Headers.ContentLength.Should().Be(bytes.Length);
            (await content.ReadAsByteArrayAsync(ct)).Should().Equal(bytes);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(file) };
        });
        var api = new DroverApi(http);

        var uploaded = await api.UploadFileWithUnverifiedSize(Guid.NewGuid(), Guid.NewGuid(), file.Name, stream,
            maxBytes: bytes.Length, cancellationToken: token);

        uploaded.Should().Be(file);
    }

    [Fact]
    public async Task A_file_over_the_server_limit_is_refused_before_the_request_is_sent()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);
        using var http = Http((_, _) => throw new InvalidOperationException("The file reached the HTTP handler."));
        var api = new DroverApi(http);

        var upload = () => api.UploadFileWithUnverifiedSize(Guid.NewGuid(), Guid.NewGuid(), "large.bin", stream,
            maxBytes: 3, cancellationToken: TestContext.Current.CancellationToken);

        var failure = (await upload.Should().ThrowAsync<DroverApiException>()).Which;
        failure.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        failure.Refusal.Should().Be(FileRefusals.TooLarge);
    }

    [Fact]
    public async Task Project_upload_uses_the_project_route_and_reports_progress()
    {
        var space = Guid.NewGuid();
        var project = Guid.NewGuid();
        var file = new FileResponse(Guid.NewGuid(), "a b.txt", "text/plain", "text", 3);
        var progress = new UploadProgress();
        using var stream = new MemoryStream([1, 2, 3]);
        using var http = Http(async (request, token) =>
        {
            var expected = $"/api/spaces/{space}/projects/{project}/files?name=a%20b.txt";
            request.RequestUri!.PathAndQuery.Should().Be(expected);
            (await request.Content!.ReadAsByteArrayAsync(token)).Should().Equal(1, 2, 3);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(file) };
        });

        var uploaded = await new DroverApi(http).UploadProjectFile(
            space, project, file.Name, stream, 3, progress, TestContext.Current.CancellationToken);

        uploaded.Should().Be(file);
        progress.Bytes.Should().Be(3);
    }

    [Fact]
    public async Task Project_upload_preserves_server_refusal_and_honors_cancellation()
    {
        using var stream = new MemoryStream([1]);
        using var cancellation = new CancellationTokenSource();
        using var http = Http((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = JsonContent.Create(new { title = "No room", refusal = FileRefusals.SpaceFull }),
            });
        });
        var api = new DroverApi(http);
        var space = Guid.NewGuid();
        var project = Guid.NewGuid();
        var refusal = await Assert.ThrowsAsync<DroverApiException>(
            () => api.UploadProjectFile(space, project, "a", stream, 1, cancellationToken: cancellation.Token));
        refusal.Refusal.Should().Be(FileRefusals.SpaceFull);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => api.UploadProjectFile(space, project, "a", stream, 1, cancellationToken: cancellation.Token));
    }

    #endregion

    #region Private Methods

    // This client reaches only the in-process request handler.
#pragma warning disable RS0030
    private static HttpClient Http(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new UploadHandler(send)) { BaseAddress = new Uri("https://drover.test/") };
#pragma warning restore RS0030

    #endregion

    #region Nested Types

    private sealed class UploadProgress : IProgress<long>
    {
        #region Properties

        public long Bytes { get; private set; }

        #endregion

        #region Interface Implementations

        public void Report(long value) => Bytes = value;

        #endregion
    }

    private sealed class UploadHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    #endregion
}
