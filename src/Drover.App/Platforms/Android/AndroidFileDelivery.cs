namespace Drover.App.Platforms.Android;

public sealed class AndroidFileDelivery(DroverApi api, DocumentDestination documents) : IFileDelivery
{
    #region Interface Implementations

    public async Task<bool> Save(SharedFile file, CancellationToken cancellationToken = default)
    {
        var staged = await Download(file, cancellationToken);
        try
        {
            if (await documents.Choose(staged.Name, MimeType(file), cancellationToken) is not { } destination)
            {
                return false;
            }

            await using var source = staged.OpenRead();
            var resolver = AndroidApplication.Context.ContentResolver!;
            await using var target = resolver.OpenOutputStream(destination, "wt")
                ?? throw new IOException("Android did not open the location you chose.");
            await source.CopyToAsync(target, cancellationToken);
            return true;
        }
        finally
        {
            staged.Directory!.Delete(recursive: true);
        }
    }

    public async Task Share(SharedFile file, CancellationToken cancellationToken = default)
    {
        var staged = await Download(file, cancellationToken);
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() => global::Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(
                new ShareFileRequest { Title = "Share file", File = new ShareFile(staged.FullName, MimeType(file)) }));
        }
        catch
        {
            staged.Directory!.Delete(recursive: true);
            throw;
        }
    }

    #endregion

    #region Private Methods

    private async Task<FileInfo> Download(SharedFile file, CancellationToken cancellationToken)
    {
        using var response = await api.OpenConversationFile(file.Href, cancellationToken);
        var root = Path.Combine(FileSystem.CacheDirectory, "sharing-root");
        Directory.CreateDirectory(root);
        ClearOldShares(root);
        var directory = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N")));
        var path = Path.Combine(directory.FullName, SafeName(file.Name));
        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(path);
            await source.CopyToAsync(output, cancellationToken);
            return new FileInfo(path);
        }
        catch
        {
            directory.Delete(recursive: true);
            throw;
        }
    }

    private static string SafeName(string name) =>
        Path.GetFileName(name.Replace('\\', '/')) is { Length: > 0 } filename && filename is not ("." or "..")
            ? filename
            : "Drover file";

    private static string MimeType(SharedFile file) =>
        MediaTypeHeaderValue.TryParse(file.ContentType, out var mediaType) && mediaType.MediaType is { } type
            ? type
            : "application/octet-stream";

    private static void ClearOldShares(string root)
    {
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (Guid.TryParseExact(Path.GetFileName(folder), "N", out _)
                && Directory.GetCreationTimeUtc(folder) < DateTime.UtcNow.AddDays(-1))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    #endregion
}
