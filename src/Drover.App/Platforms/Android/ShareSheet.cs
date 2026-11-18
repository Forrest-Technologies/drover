namespace Drover.App.Platforms.Android;

/// <summary>Opens Android's share sheet for a file in the app's cache, through the platform's own share call.</summary>
internal static class ShareSheet
{
    public static Task Open(string title, string path, string contentType) =>
        Share.Default.RequestAsync(new ShareFileRequest { Title = title, File = new ShareFile(path, contentType) });
}
