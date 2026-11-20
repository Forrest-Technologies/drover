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
    public async Task Project_file_client_methods_upload_with_progress_list_remove_and_build_addresses()
    {
        var token = TestContext.Current.CancellationToken;
        var spaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var bytes = new byte[100_000];
        var file = new FileResponse(Guid.NewGuid(), "guide.pdf", "application/pdf", FileKinds.Other, bytes.Length);
        var projectFile = new ProjectFileResponse(file, projectId, DateTimeOffset.UtcNow);
        List<long> reported = [];

        using var http = Http(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                request.RequestUri!.PathAndQuery.Should().Be($"/api/spaces/{spaceId}/projects/{projectId}/files?name=guide.pdf");
                request.Headers.GetValues("X-XSRF-TOKEN").Should().Equal("token-1");
                var body = await request.Content!.ReadAsByteArrayAsync(ct);
                body.Length.Should().Be(bytes.Length);
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(projectFile) };
            }

            if (request.Method == HttpMethod.Get)
            {
                request.RequestUri!.PathAndQuery.Should().Be($"/api/spaces/{spaceId}/projects/{projectId}/files");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create<IReadOnlyList<ProjectFileResponse>>([projectFile]) };
            }

            if (request.Method == HttpMethod.Delete)
            {
                request.RequestUri!.PathAndQuery.Should().Be($"/api/spaces/{spaceId}/projects/{projectId}/files/{file.Id}");
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected request {request.Method} {request.RequestUri}");
        });

        var api = new DroverApi(http) { AntiforgeryToken = "token-1" };
        using var uploadStream = new MemoryStream(bytes);
        var uploaded = await api.UploadProjectFile(spaceId, projectId, "guide.pdf", uploadStream, bytes.Length, new SynchronousProgress(reported.Add), token);
        var listed = await api.ListProjectFiles(spaceId, projectId, token);
        await api.RemoveProjectFile(spaceId, projectId, file.Id, token);

        uploaded.Should().Be(projectFile);
        reported.Should().NotBeEmpty().And.BeInAscendingOrder();
        reported[^1].Should().Be(bytes.Length);
        listed.Should().ContainSingle().Which.Should().Be(projectFile);
        api.ProjectFileAddress(spaceId, projectId, file.Id).Should().Be($"https://drover.test/api/spaces/{spaceId}/projects/{projectId}/files/{file.Id}");
        api.ProjectFileAddress(spaceId, projectId, file.Id, download: true).Should().Be($"https://drover.test/api/spaces/{spaceId}/projects/{projectId}/files/{file.Id}?download=true");
    }

    [Fact]
    public async Task Project_file_upload_with_unverified_size_cancellation_and_server_errors_are_handled()
    {
        var token = TestContext.Current.CancellationToken;
        var spaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var bytes = "project data"u8.ToArray();
        var projectFile = new ProjectFileResponse(
            new FileResponse(Guid.NewGuid(), "data.txt", "text/plain", FileKinds.Text, bytes.Length),
            projectId,
            DateTimeOffset.UtcNow);

        using var http = Http(async (request, ct) =>
        {
            if (request.RequestUri!.Query.Contains("oversized", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
                {
                    Content = JsonContent.Create(new { title = "Space is full.", refusal = FileRefusals.SpaceFull }),
                };
            }

            request.Content!.Headers.ContentLength.Should().Be(bytes.Length);
            (await request.Content.ReadAsByteArrayAsync(ct)).Should().Equal(bytes);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(projectFile) };
        });

        var api = new DroverApi(http);
        using var okStream = new MemoryStream(bytes);
        var uploaded = await api.UploadProjectFileWithUnverifiedSize(spaceId, projectId, "data.txt", okStream, bytes.Length, cancellationToken: token);
        uploaded.Should().Be(projectFile);

        using var tooLargeStream = new MemoryStream([1, 2, 3, 4]);
        var clientRefused = () => api.UploadProjectFileWithUnverifiedSize(spaceId, projectId, "big.bin", tooLargeStream, maxBytes: 3, cancellationToken: token);
        (await clientRefused.Should().ThrowAsync<DroverApiException>()).Which.Refusal.Should().Be(FileRefusals.TooLarge);

        using var serverRefusedStream = new MemoryStream(bytes);
        var serverRefused = () => api.UploadProjectFile(spaceId, projectId, "oversized.txt", serverRefusedStream, bytes.Length, cancellationToken: token);
        var failure = (await serverRefused.Should().ThrowAsync<DroverApiException>()).Which;
        failure.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        failure.Refusal.Should().Be(FileRefusals.SpaceFull);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var cancelledStream = new MemoryStream(bytes);
        var cancelled = () => api.UploadProjectFile(spaceId, projectId, "cancel.txt", cancelledStream, bytes.Length, cancellationToken: cts.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
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

    private sealed class UploadHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class SynchronousProgress(Action<long> report) : IProgress<long>
    {
        public void Report(long value) => report(value);
    }

    #endregion
}
