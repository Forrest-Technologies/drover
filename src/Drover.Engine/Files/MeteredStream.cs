namespace Drover.Engine.Files;

/// <summary>
/// Reads an upload once, on its way to the file store: counts it, hashes it, and shows every byte to the inspector.
/// Past <paramref name="maxBytes"/> it stops with <see cref="FileTooLargeException"/>, so a body without a declared
/// length is cut off at the limit instead of filling the disk.
/// </summary>
public sealed class MeteredStream(Stream inner, long maxBytes, FileInspector inspector) : Stream
{
    #region Private Fields

    private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    #endregion

    #region Properties

    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    #endregion

    #region Public Methods

    /// <summary>The SHA-256 of every byte read, in lowercase hexadecimal. Call once, after the last read.</summary>
    public string FinishHash() => Convert.ToHexStringLower(hash.GetHashAndReset());

    #endregion

    #region Overrides

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => Take(buffer[..inner.Read(buffer)]);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        return Take(buffer.Span[..read]);
    }

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
            hash.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion

    #region Private Methods

    private int Take(ReadOnlySpan<byte> read)
    {
        BytesRead += read.Length;

        if (BytesRead > maxBytes)
        {
            throw new FileTooLargeException();
        }

        hash.AppendData(read);
        inspector.Append(read);
        return read.Length;
    }

    #endregion
}

/// <summary>An upload went past the largest file the server takes.</summary>
public sealed class FileTooLargeException() : IOException("The file is larger than this server takes.");
