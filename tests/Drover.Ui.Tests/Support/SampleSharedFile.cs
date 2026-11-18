namespace Drover.Ui.Tests.Support;

/// <summary>A file another app shared, as a native shell hands it over.</summary>
public sealed class SampleSharedFile(string name, string words) : IBrowserFile, IPickedFile
{
    #region Private Fields

    private readonly byte[] content = Encoding.UTF8.GetBytes(words);

    #endregion

    #region Interface Implementations

    public string Name => name;

    public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

    public long Size => content.Length;

    public string ContentType => "text/plain";

    public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream(content);

    public Stream OpenRead(long maxBytes, CancellationToken cancellationToken) => new MemoryStream(content);

    #endregion
}
