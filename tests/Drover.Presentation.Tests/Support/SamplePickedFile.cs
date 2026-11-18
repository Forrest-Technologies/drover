namespace Drover.Presentation.Tests.Support;

/// <summary>A file a person picked, as a client hands it over. Its name and words are made up.</summary>
public sealed class SamplePickedFile(string name, byte[] content, bool sizeKnown = true) : IPickedFile
{
    #region Constructors

    public SamplePickedFile(string name, string words, bool sizeKnown = true)
        : this(name, Encoding.UTF8.GetBytes(words), sizeKnown)
    {
    }

    #endregion

    #region Interface Implementations

    public string Name => name;

    public long Size => sizeKnown ? content.Length : 0;

    public Task<Stream> OpenRead(long maxBytes, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream(content));

    #endregion
}
