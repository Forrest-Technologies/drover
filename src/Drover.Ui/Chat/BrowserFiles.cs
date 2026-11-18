namespace Drover.Ui.Chat;

/// <summary>
/// The file a browser hands over, for a draft that holds picked files of any client. A shared file that began as a
/// browser file is given back as it was.
/// </summary>
public static class BrowserFiles
{
    #region Public Methods

    public static IBrowserFile Of(IPickedFile file) => file as IBrowserFile ?? new PickedBrowserFile(file);

    #endregion

    #region Nested Types

    private sealed class PickedBrowserFile(IPickedFile file) : IBrowserFile
    {
        #region Interface Implementations

        public string Name => file.Name;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => file.Size;

        public string ContentType => string.Empty;

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            file.OpenRead(maxAllowedSize, cancellationToken);

        #endregion
    }

    #endregion
}
