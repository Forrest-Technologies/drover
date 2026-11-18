namespace Drover.Native.Platform;

/// <summary>
/// A conversation file, out of the app on a desktop. Saving asks the system's save dialog where it goes; nothing is
/// written anywhere the person did not choose. A desktop has no share sheet, so sharing puts a copy in a folder of
/// the app's own and opens that folder, from where the person drags or attaches it. The file itself is never opened:
/// a bot can send a program, and opening it would run it. A failure is an <see cref="IOException"/>, which the file
/// card turns into a sentence.
/// </summary>
internal sealed class DesktopFileDelivery(DroverApi api, ILogger<DesktopFileDelivery> logger) : IFileDelivery
{
    #region Private Fields

    private const string Folder = "drover-opened-files";

    #endregion

    #region Interface Implementations

    public async Task<bool> Save(SharedFile file, CancellationToken cancellationToken = default)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(SafeName(file.Name)),
        };
        picker.FileTypeChoices.Add(TypeName(file.Name), [Extension(file.Name)]);

        Windows.Storage.StorageFile? destination;
        try
        {
            destination = await picker.PickSaveFileAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or UnauthorizedAccessException)
        {
            throw new IOException("The system's save dialog could not be opened.", exception);
        }

        if (destination is null)
        {
            return false;
        }

        using var response = await api.OpenConversationFile(file.Href, cancellationToken);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination.Path);
        await source.CopyToAsync(target, cancellationToken);
        return true;
    }

    public async Task Share(SharedFile file, CancellationToken cancellationToken = default)
    {
        var place = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Folder, Guid.NewGuid().ToString("N"));
        var folder = Directory.CreateDirectory(place);
        var path = System.IO.Path.Combine(folder.FullName, SafeName(file.Name));
        using (var response = await api.OpenConversationFile(file.Href, cancellationToken))
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(path);
            await source.CopyToAsync(target, cancellationToken);
        }

        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri(folder.FullName)))
            {
                throw new IOException("The folder with the file could not be opened.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(exception, "The system could not open the folder of a conversation file");
            throw new IOException("The folder with the file could not be opened.", exception);
        }
    }

    #endregion

    #region Private Methods

    // The name comes from the conversation. Only its last part is used, inside a folder of this app's own.
    private static string SafeName(string name) =>
        System.IO.Path.GetFileName(name.Replace('\\', '/')) is { Length: > 0 } filename && filename is not ("." or "..")
            ? filename
            : "Drover file";

    private static string Extension(string name) => System.IO.Path.GetExtension(SafeName(name)) is { Length: > 1 } extension ? extension : ".bin";

    private static string TypeName(string name) => $"{Extension(name).TrimStart('.').ToUpperInvariant()} file";

    #endregion
}
