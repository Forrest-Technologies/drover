namespace Drover.Native.Chat;

/// <summary>
/// Files a person hands to a conversation on this device: chosen in the platform's file chooser, pasted, or
/// dropped. Each becomes a picked file the conversation uploads, whichever way it came. Only the first files of one go
/// are read; the rest are counted, so the person can be told.
/// </summary>
internal static class PickedFiles
{
    #region Private Fields

    // The formats a desktop may name a picture's bytes by, best first, when it does not hand the picture over as a
    // bitmap. A Linux desktop's clipboard (X11, and Wayland through it) works this way.
    private static readonly string[] pictureTypes =
        ["image/png", "image/bmp", "image/jpeg", "image/gif", "image/webp", "image/tiff"];

    // Words in a format's name that say it holds a picture, for one the device lists and does not hand over.
    private static readonly string[] pictureWords = ["image", "png", "jpeg", "bitmap", "dib", "tiff"];

    #endregion

    #region Public Methods

    /// <summary>
    /// Opens the device's file chooser for a message: the first files one go takes are read, and the rest counted.
    /// Nothing chosen is nothing handed over.
    /// </summary>
    public static async Task<HandedFiles> Choose() => await HandedFiles.ReadFirst(await OpenChooser(), Described);

    /// <summary>Opens the device's file chooser for a project, which takes every file chosen.</summary>
    public static async Task<IReadOnlyList<IPickedFile>> ChooseAll()
    {
        List<IPickedFile> files = [];

        foreach (var file in await OpenChooser())
        {
            files.Add(await Described(file));
        }

        return files;
    }

    /// <summary>
    /// What the clipboard holds for a paste into the message field. Text wins: a copy from an office program carries
    /// its text and a picture of it, and then the text is what was meant.
    /// </summary>
    public static ClipboardHolds Holds(Windows.ApplicationModel.DataTransfer.DataPackageView carried)
    {
        if (carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            return ClipboardHolds.Text;
        }

        if (carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
            || carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap)
            || PictureType(carried) is not null)
        {
            return ClipboardHolds.Files;
        }

        return carried.AvailableFormats.Any(IsPictureFormat)
            ? ClipboardHolds.PictureNotHandedOver
            : ClipboardHolds.Nothing;
    }

    /// <summary>
    /// What was pasted: its files, with the folders counted and left out, since a message carries files. A picture
    /// with no file behind it, such as a screenshot, is taken as a PNG. A picture that cannot be read throws.
    /// </summary>
    public static async Task<HandedFiles> Pasted(Windows.ApplicationModel.DataTransfer.DataPackageView carried)
    {
        if (carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            return await Stored(carried);
        }

        if (carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap))
        {
            return new([await Bitmap(carried)]);
        }

        return PictureType(carried) is { } type ? new([await NamedPicture(carried, type)]) : new([]);
    }

    /// <summary>
    /// What was dropped: its files, with the folders counted and left out. Only storage items are read, as only they
    /// let the drop be accepted while it was held over the conversation.
    /// </summary>
    public static async Task<HandedFiles> Dropped(Windows.ApplicationModel.DataTransfer.DataPackageView carried) =>
        carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
            ? await Stored(carried)
            : new([]);

    /// <summary>
    /// A file held in memory: a picture from the clipboard as a PNG, or a file the browser read for a paste. Its size
    /// is what the server is asked to take.
    /// </summary>
    public static IPickedFile FromBytes(byte[] bytes, string name) => new Held(name, bytes);

    /// <summary>
    /// A file too large to be read in here. It carries its size, so the chip says it is larger than the server takes,
    /// and it holds nothing to send.
    /// </summary>
    public static IPickedFile TooLargeToRead(string name, long size) => new Unread(name, size);

    #endregion

    #region Private Methods

    private static async Task<IReadOnlyList<Windows.Storage.StorageFile>> OpenChooser()
    {
        var chooser = new Windows.Storage.Pickers.FileOpenPicker();
        chooser.FileTypeFilter.Add("*");
        return await chooser.PickMultipleFilesAsync() ?? [];
    }

    private static async Task<HandedFiles> Stored(Windows.ApplicationModel.DataTransfer.DataPackageView carried)
    {
        var items = await carried.GetStorageItemsAsync();
        List<Windows.Storage.StorageFile> files = [.. items.OfType<Windows.Storage.StorageFile>()];
        return await HandedFiles.ReadFirst(files, Described, items.Count - files.Count);
    }

    // Android providers can leave the size blank or report a different size from the opened stream.
    private static async Task<IPickedFile> Described(Windows.Storage.StorageFile file) =>
        new Picked(file, OperatingSystem.IsAndroid() ? 0 : (long)(await file.GetBasicPropertiesAsync()).Size);

    // A bitmap, as Windows and macOS hand a screenshot over. Its stream is read up to the limit and no further.
    private static async Task<IPickedFile> Bitmap(Windows.ApplicationModel.DataTransfer.DataPackageView carried)
    {
        var name = PictureName();
        using var stream = await (await carried.GetBitmapAsync()).OpenReadAsync();
        var size = (long)stream.Size;
        var read = await PastedPicture.Read(
            stream.AsStreamForRead(0),
            size,
            PastedPicture.Limit,
            CancellationToken.None);

        return read is null
            ? TooLargeToRead(name, Math.Max(size, PastedPicture.Limit + 1L))
            : await Converted(read, name);
    }

    // A picture's bytes under a format's name, as a Linux desktop hands a screenshot over. The platform reads them
    // whole before they arrive here, so the limit can only keep a large one from being decoded and sent.
    private static async Task<IPickedFile> NamedPicture(
        Windows.ApplicationModel.DataTransfer.DataPackageView carried,
        string type)
    {
        var name = PictureName();
        var bytes = await carried.GetDataAsync(type) as byte[]
            ?? throw new InvalidDataException("The clipboard did not hand over the bytes of the picture it listed.");

        return bytes.Length > PastedPicture.Limit ? TooLargeToRead(name, bytes.Length) : await Converted(bytes, name);
    }

    private static async Task<IPickedFile> Converted(byte[] read, string name)
    {
        var png = await Task.Run(() => PastedPicture.ToPng(read));
        return png is null
            ? throw new InvalidDataException("The clipboard's picture is not one still picture of a size to attach.")
            : FromBytes(png, name);
    }

    private static string? PictureType(Windows.ApplicationModel.DataTransfer.DataPackageView carried) =>
        pictureTypes.FirstOrDefault(carried.Contains);

    private static bool IsPictureFormat(string format) =>
        pictureWords.Any(word => format.Contains(word, StringComparison.OrdinalIgnoreCase));

    private static string PictureName() => FileWords.PastedPictureName(TimeProvider.System.GetLocalNow());

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
