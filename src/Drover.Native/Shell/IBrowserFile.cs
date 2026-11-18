namespace Drover.Native.Shell;

/// <summary>
/// A file another app shared to Drover, waiting in the share inbox. It has the shape of the file type the web
/// client's share inbox holds, so the inbox and the Android share code are one source in both clients
/// (src/Shared/Chat/ShareInbox.cs, src/Shared/Android/AndroidShares.cs).
/// </summary>
public interface IBrowserFile
{
    string Name { get; }

    DateTimeOffset LastModified { get; }

    long Size { get; }

    string ContentType { get; }

    Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default);
}
