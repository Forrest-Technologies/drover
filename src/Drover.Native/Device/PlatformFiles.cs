namespace Drover.Native.Platform;

/// <summary>Save an extension through the platform picker, without choosing a path on the person's behalf.</summary>
internal static class PlatformFiles
{
    public static async Task<bool> SaveText(string name, string text)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = name,
            };
            picker.FileTypeChoices.Add("JSON file", [".json"]);
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
}
