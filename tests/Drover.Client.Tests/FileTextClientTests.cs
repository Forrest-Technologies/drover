namespace Drover.Client.Tests;

/// <summary>
/// The typed client's read of a text file's whole content for "Show all": bounded, saying whether there is more, and
/// reading the response only asynchronously, as a browser's response stream allows.
/// </summary>
public sealed class FileTextClientTests
{
    #region Public Methods

    [Theory]
    [InlineData("abcde", 5, "abcde", false)]
    [InlineData("abcdef", 5, "abcde", true)]
    [InlineData("abc", 5, "abc", false)]
    public async Task A_text_file_is_read_up_to_its_bound_and_says_whether_it_goes_on(string content, int bound, string shown, bool cut)
    {
        var spaceId = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();
        var server = new TextHandler(content);
        var api = new DroverApi(Http(server));

        var text = await api.ReadFileText(spaceId, fileId, bound, TestContext.Current.CancellationToken);

        text.Should().Be(new FileText(shown, cut));
        server.PathAndQuery.Should().Be($"/api/spaces/{spaceId}/files/{fileId}");
    }

    #endregion

    #region Private Methods

    // The banned constructor guards outbound calls in the product. This client reaches only the handler.
#pragma warning disable RS0030
    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://drover.test/") };
#pragma warning restore RS0030

    #endregion

    #region Nested Types

    private sealed class TextHandler(string content) : HttpMessageHandler
    {
        #region Properties

        public string? PathAndQuery { get; private set; }

        #endregion

        #region Protected Methods

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            PathAndQuery = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new AsyncOnlyStream(Encoding.UTF8.GetBytes(content))),
            });
        }

        #endregion
    }

    /// <summary>A response body that, like a browser's, cannot be read synchronously.</summary>
    private sealed class AsyncOnlyStream(byte[] bytes) : Stream
    {
        #region Private Fields

        private readonly MemoryStream inner = new(bytes, writable: false);

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        #endregion

        #region Overrides

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous reads are not supported.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        #endregion
    }

    #endregion
}
