namespace Drover.Presentation.Tests.Support;

/// <summary>A file a person picked, as a client hands it over. Its name and words are made up.</summary>
public sealed class SamplePickedFile(string name, byte[] content) : IPickedFile
{
    #region Constructors

    public SamplePickedFile(string name, string words)
        : this(name, Encoding.UTF8.GetBytes(words))
    {
    }

    #endregion

    #region Interface Implementations

    public string Name => name;

    public long Size => content.Length;

    public Task<Stream> OpenRead(long maxBytes, CancellationToken cancellationToken) => Task.FromResult<Stream>(new MemoryStream(content));

    #endregion
}
