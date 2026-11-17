namespace Drover.Engine.Sandboxes;

/// <summary>
/// Reads a file out of a sandbox and stops with <see cref="BuiltinToolException"/> when the sandbox's access token is
/// in it, even split across two reads. Text a sandbox prints is scrubbed of the token; a file's bytes are kept as they
/// are, so a file that holds the token is not kept at all.
/// </summary>
public sealed class TokenGuardStream(Stream inner, string token) : Stream
{
    #region Private Fields

    private readonly byte[] needle = Encoding.UTF8.GetBytes(token);

    /// <summary>The end of what was read so far, one byte shorter than the token, so a token split across reads is found.</summary>
    private byte[] tail = [];

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

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => Check(buffer[..inner.Read(buffer)]);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        return Check(buffer.Span[..read]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    #endregion

    #region Private Methods

    private int Check(ReadOnlySpan<byte> read)
    {
        if (needle.Length == 0 || read.Length == 0)
        {
            return read.Length;
        }

        byte[] seen = [.. tail, .. read];

        if (seen.AsSpan().IndexOf(needle) >= 0)
        {
            throw new BuiltinToolException("The file holds this sandbox's access key, so it was not shared.");
        }

        tail = seen[Math.Max(0, seen.Length - (needle.Length - 1))..];
        return read.Length;
    }

    #endregion
}
