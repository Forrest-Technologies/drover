namespace Drover.Native.Chat;

/// <summary>
/// What a drop or a paste carried: the files among it, and how many folders it left out, so a drop of folders alone
/// can say that a message carries files.
/// </summary>
internal sealed record Taken(IReadOnlyList<IPickedFile> Files, int Folders);

/// <summary>
/// Files a person hands to a conversation on this device: chosen in the platform's file chooser, pasted, or
/// dropped. Each becomes a picked file the conversation uploads, whichever way it came.
/// </summary>
internal static class PickedFiles
{
    #region Private Fields

    // The browser names a picture from the clipboard "image.png" whatever it shows, which says nothing to the person.
    private const string BrowsersPictureName = "image.png";

    #endregion

    #region Public Methods

    /// <summary>Opens the device's file chooser. Nothing chosen is an empty list.</summary>
    public static async Task<IReadOnlyList<IPickedFile>> Choose()
    {
        var chooser = new Windows.Storage.Pickers.FileOpenPicker();
        chooser.FileTypeFilter.Add("*");
        return await From(await chooser.PickMultipleFilesAsync());
    }

    /// <summary>
    /// What was pasted or dropped: its files, with the folders counted and left out, since a message carries files. A
    /// picture with no file behind it, such as a screenshot, is taken as a PNG. A picture that cannot be read throws.
    /// </summary>
    public static async Task<Taken> From(Windows.ApplicationModel.DataTransfer.DataPackageView carried)
    {
        if (carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            var items = await carried.GetStorageItemsAsync();
            var files = await From(items.OfType<Windows.Storage.StorageFile>());
            return new(files, items.Count - files.Count);
        }

        return carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap)
            ? new([await Picture(carried)], 0)
            : new([], 0);
    }

    /// <summary>A picture held in memory, as a PNG named for the person. Its size is what the server is asked to take.</summary>
    public static IPickedFile FromPicture(byte[] png, string name) => new Held(name, png);

    /// <summary>
    /// A file the browser handed over with the page's paste event. A name the browser made up for a picture, or none,
    /// is replaced by one that says when it was pasted.
    /// </summary>
    public static IPickedFile FromPasted(string name, byte[] bytes) =>
        new Held(Named(name), bytes);

    /// <summary>
    /// A file too large to be read in here. It carries its size, so the chip says it is larger than the server takes,
    /// and it holds nothing to send.
    /// </summary>
    public static IPickedFile TooLargeToRead(string name, long size) => new Unread(name, size);

    #endregion

    #region Private Methods

    private static async Task<IReadOnlyList<IPickedFile>> From(IEnumerable<Windows.Storage.StorageFile>? files)
    {
        List<IPickedFile> picked = [];

        foreach (var file in files ?? [])
        {
            // Android providers can leave the size blank or report a different size from the opened stream.
            var size = OperatingSystem.IsAndroid() ? 0 : (long)(await file.GetBasicPropertiesAsync()).Size;
            picked.Add(new Picked(file, size));
        }

        return picked;
    }

    private static async Task<IPickedFile> Picture(Windows.ApplicationModel.DataTransfer.DataPackageView carried)
    {
        var name = FileWords.PastedPictureName(TimeProvider.System.GetLocalNow());
        using var stream = await (await carried.GetBitmapAsync()).OpenReadAsync();
        var read = await PastedPicture.Read(stream.AsStreamForRead(), (long)stream.Size, CancellationToken.None);

        if (read is null)
        {
            return TooLargeToRead(name, Math.Max((long)stream.Size, PastedPicture.Limit + 1));
        }

        var png = await Task.Run(() => PastedPicture.ToPng(read));
        return png is null
            ? throw new InvalidDataException("The clipboard's picture is not a single picture of a size that can be attached.")
            : FromPicture(png, name);
    }

    private static string Named(string name) =>
        string.IsNullOrWhiteSpace(name) || name.Equals(BrowsersPictureName, StringComparison.OrdinalIgnoreCase)
            ? FileWords.PastedPictureName(TimeProvider.System.GetLocalNow())
            : name;

    #endregion

    #region Nested Types

    private sealed class Picked(Windows.Storage.StorageFile file, long size) : IPickedFile
    {
        #region Interface Implementations

        public string Name => file.Name;

        public long Size => size;

        public Task<Stream> OpenRead(long maxBytes, CancellationToken cancellationToken) => file.OpenStreamForReadAsync();

        #endregion
    }

    // Bytes already in memory: a picture from the clipboard, or a file the browser read. Its size is known exactly.
    private sealed class Held(string name, byte[] bytes) : IPickedFile
    {
        #region Interface Implementations

        public string Name => name;

        public long Size => bytes.Length;

        public Task<Stream> OpenRead(long maxBytes, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));

        #endregion
    }

    private sealed class Unread(string name, long size) : IPickedFile
    {
        #region Interface Implementations

        public string Name => name;

        public long Size => size;

        public Task<Stream> OpenRead(long maxBytes, CancellationToken cancellationToken) =>
            throw new IOException($"{name} is too large to be read here.");

        #endregion
    }

    #endregion
}
