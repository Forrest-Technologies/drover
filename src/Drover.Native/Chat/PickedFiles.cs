namespace Drover.Native.Chat;

/// <summary>
/// Files a person hands to a conversation on this device: chosen in the platform's file chooser, pasted, or
/// dropped. Each becomes a picked file the conversation uploads, whichever way it came.
/// </summary>
internal static class PickedFiles
{
    #region Public Methods

    /// <summary>Opens the device's file chooser. Nothing chosen is an empty list.</summary>
    public static async Task<IReadOnlyList<IPickedFile>> Choose()
    {
        var chooser = new Windows.Storage.Pickers.FileOpenPicker();
        chooser.FileTypeFilter.Add("*");
        return await From(await chooser.PickMultipleFilesAsync());
    }

    /// <summary>The files among what was pasted or dropped. Folders are left out: a message carries files.</summary>
    public static async Task<IReadOnlyList<IPickedFile>> From(Windows.ApplicationModel.DataTransfer.DataPackageView carried) =>
        carried.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)
            ? await From((await carried.GetStorageItemsAsync()).OfType<Windows.Storage.StorageFile>())
            : [];

    #endregion

    #region Private Methods

    private static async Task<IReadOnlyList<IPickedFile>> From(IEnumerable<Windows.Storage.StorageFile>? files)
    {
        List<IPickedFile> picked = [];

        foreach (var file in files ?? [])
        {
            var facts = await file.GetBasicPropertiesAsync();
            picked.Add(new Picked(file, (long)facts.Size));
        }

        return picked;
    }

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

    #endregion
}
