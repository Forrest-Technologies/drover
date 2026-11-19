namespace Drover.Native.Platform;

/// <summary>Save text through the platform picker, without choosing a path on the person's behalf.</summary>
internal static class PlatformFiles
{
    #region Public Methods

    public static Task<bool> SaveText(string name, string text) => Save(name, "JSON file", ".json", text);

    /// <summary>
    /// Saves text under the name, offering the name's extension as its only type, named for its media type. The
    /// picker adds the extension, so it is taken off the suggested name.
    /// </summary>
    public static Task<bool> SaveText(string name, string text, string mediaType)
    {
        var extension = System.IO.Path.GetExtension(name) is { Length: > 1 } found ? found : ".txt";
        var typeName = mediaType switch
        {
            "text/markdown" => "Markdown file",
            "application/json" => "JSON file",
            _ => "Text file",
        };
        return Save(System.IO.Path.GetFileNameWithoutExtension(name), typeName, extension, text);
    }

    #endregion

    #region Private Methods

    private static async Task<bool> Save(string suggestedName, string typeName, string extension, string text)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                SuggestedFileName = suggestedName,
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
