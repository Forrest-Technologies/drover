namespace Drover.Native.Platform;

/// <summary>
/// Save an extension or the diagnostic log through the platform picker, without choosing a path on the person's
/// behalf.
/// </summary>
internal static class PlatformFiles
{
    #region Public Methods

    public static Task<bool> SaveText(string name, string text) => Save(name, text, "JSON file", ".json");

    public static Task<bool> SaveLog(string name, string text) =>
        Save(System.IO.Path.GetFileNameWithoutExtension(name), text, "Text file", ".txt");

    #endregion

    #region Private Methods

    private static async Task<bool> Save(string name, string text, string typeName, string extension)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = name,
            };
            picker.FileTypeChoices.Add(typeName, [extension]);
            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return false;
            }

            Windows.Storage.CachedFileManager.DeferUpdates(file);
            await Windows.Storage.FileIO.WriteTextAsync(file, text);
            var result = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);
            if (result is not Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                throw new IOException("The platform did not complete the file update.");
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            throw new DeviceCallFailedException("The platform refused to save the file.", exception);
        }
    }

    #endregion
}
