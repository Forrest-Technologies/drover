namespace Drover.Ui.Chat;

/// <summary>
/// A file a person picked, pasted, dropped, or shared from another app, before it is uploaded: its name, its size,
/// and a way to read it. Each client hands its own kind of file over as this, so what happens to a file next is
/// written once for all of them.
/// </summary>
public interface IPickedFile
{
    /// <summary>The file's name, as the person knows it.</summary>
    string Name { get; }

    /// <summary>How many bytes it holds.</summary>
    long Size { get; }

    /// <summary>Opens the file to be read once, from its start. A file larger than <paramref name="maxBytes"/> may refuse.</summary>
    Stream OpenRead(long maxBytes, CancellationToken cancellationToken);
}
