namespace Drover.Native.Droid;

/// <summary>
/// Opens Android's share sheet for a file in the app's cache. The receiving app is given a content address with
/// permission to read that one file, through the app's file provider, which exposes only the cache folder that
/// conversation files are staged in (Resources/xml/drover_file_paths.xml).
/// </summary>
internal static class ShareSheet
{
    #region Public Methods

    public static Task Open(string title, string path, string contentType)
    {
        var context = Platform.CurrentActivity ?? throw new InvalidOperationException("Drover has no window to share from.");
        using var file = new Java.IO.File(path);
        var address = AndroidX.Core.Content.FileProvider.GetUriForFile(context, $"{context.PackageName}.files", file)!;
        using var send = new Intent(Intent.ActionSend)
            .SetType(contentType)!
            .PutExtra(Intent.ExtraStream, address)!
            .AddFlags(ActivityFlags.GrantReadUriPermission)!;
        using var chooser = Intent.CreateChooser(send, title)!;
        context.StartActivity(chooser);
        return Task.CompletedTask;
    }

    #endregion
}
