namespace Drover.Adapters.Mcp;

/// <summary>An answer from a tool server was larger than this server accepts. It was not read to the end.</summary>
public sealed class ToolResponseTooLargeException(int limit)
    : HttpRequestException($"The tool server's answer was larger than the {limit} bytes this server accepts.");

/// <summary>
/// The most bytes of one answer the tool source reads, for the call in flight. A tool server decides how much it
/// sends; without a bound, one answer could take the whole server's memory, for every space. The bound is set around
/// each use of a client and read by <see cref="ResponseLimitHandler"/>, which every request of the adapter passes.
/// It also remembers that it was reached: the protocol library reports a body that stopped early in its own words,
/// so the handler's failure is not always the one the caller sees.
/// </summary>
public sealed class ResponseLimit(int maxBytes)
{
    #region Private Fields

    private static readonly AsyncLocal<ResponseLimit?> current = new();

    private int reached;

    #endregion

    #region Properties

    /// <summary>The bound in force, or the self-hosted default where none was stated.</summary>
    public static ResponseLimit Current => current.Value ?? new ResponseLimit(ToolServerLimits.Default.MaxResponseBytes);

    public int MaxBytes => maxBytes;

    public bool WasReached => Volatile.Read(ref reached) == 1;

    #endregion

    #region Public Methods

    /// <summary>Starts a bound for the work that follows in this flow, and for a session that work opens.</summary>
    public static ResponseLimit Begin(int maxBytes) => current.Value = new ResponseLimit(maxBytes);

    public ToolResponseTooLargeException Reach()
    {
        Volatile.Write(ref reached, 1);
        return new ToolResponseTooLargeException(maxBytes);
    }

    #endregion
}

/// <summary>
/// Refuses an answer that says it is larger than the bound, and stops reading one that turns out to be. The body is
/// never buffered here: it is counted as the caller reads it.
/// </summary>
public sealed class ResponseLimitHandler : DelegatingHandler
{
    #region Protected Methods

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var limit = ResponseLimit.Current;
        var response = await base.SendAsync(request, cancellationToken);

        if (response.Content.Headers.ContentLength > limit.MaxBytes)
        {
            response.Dispose();
            throw limit.Reach();
        }

        var bounded = new StreamContent(new BoundedStream(await response.Content.ReadAsStreamAsync(cancellationToken), limit));

        foreach (var header in response.Content.Headers)
        {
            bounded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = bounded;
        return response;
    }

    #endregion

    #region Nested Types

    private sealed class BoundedStream(Stream inner, ResponseLimit limit) : Stream
    {
        #region Private Fields

        private long read;

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => read;
            set => throw new NotSupportedException();
        }

        #endregion

        #region Overrides

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken));

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

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

        #region Private Methods

        private int Count(int bytes)
        {
            read += bytes;
            return read > limit.MaxBytes ? throw limit.Reach() : bytes;
        }

        #endregion
    }

    #endregion
}
