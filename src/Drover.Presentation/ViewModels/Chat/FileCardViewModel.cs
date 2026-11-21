namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// One file in a conversation. A picture is shown with its dimensions set; a text file shows its first lines, and all
/// of it on "Show all"; anything else is a card. Every form has the same way to get the file. A bot HTML file can ask
/// for an isolated preview address; the platform surface decides whether it can enforce the required containment.
/// </summary>
public sealed class FileCardViewModel(ILogger<FileCardViewModel> logger, IFileDelivery? delivery = null) : ObservableObject
{
    #region Private Fields

    private FileText? whole;

    private bool focusWhole;

    private string? previewGrant;

    private int previewGeneration;

    #endregion

    #region Properties

    public SharedFile File { get; set; } = null!;

    /// <summary>Reads a text file's whole content, up to <see cref="FileWords.MaxShownCharacters"/>. Without it there is no "Show all".</summary>
    public Func<SharedFile, Task<FileText>>? ReadWhole { get; set; }

    /// <summary>Issues one isolated address for a bot HTML file. The client surface decides whether it can contain it.</summary>
    public Func<SharedFile, Task<FilePreviewResponse>>? RequestPreview { get; set; }

    /// <summary>Invalidates a preview grant that was not consumed before its surface closed.</summary>
    public Func<SharedFile, string, Task>? RevokePreview { get; set; }

    public FileKind Kind => FileWords.Kind(File);

    /// <summary>What a picture is called for someone who cannot see it.</summary>
    public string PictureName => File.Caption ?? File.Name;

    /// <summary>The text shown: the file's first lines, or all of it once it was read.</summary>
    public string? PreviewText => whole?.Text ?? File.Preview;

    public string PreviewName => whole is null ? $"The first lines of {File.Name}" : $"All of {File.Name}";

    /// <summary>Said when the whole file is longer than what is shown.</summary>
    public string? CutNote => whole is { Cut: true } ? FileWords.Cut(File.Name) : null;

    public bool CanShowAll => ReadWhole is not null && whole is null && FileWords.HasMore(File);

    /// <summary>True while the whole text is read. The button stays in place, and a second press does nothing.</summary>
    public bool Reading { get; private set; }

    public string ShowAllLabel => Reading ? "Loading" : "Show all";

    public string ShowAllName => Reading ? $"Loading all of {File.Name}" : $"Show all of {File.Name}";

    /// <summary>Why the rest could not be loaded, as a sentence.</summary>
    public string? Problem { get; private set; }

    public string Facts => FileWords.Facts(File);

    /// <summary>True on a device, where a file is saved or shared through the device instead of downloaded by a browser.</summary>
    public bool Delivers => delivery is not null;

    public string SaveName => $"Save {File.Name}";

    public string ShareName => $"Share {File.Name}";

    public string DownloadName => $"Download {File.Name}";

    public bool Exporting { get; private set; }

    /// <summary>How saving or sharing ended, as a sentence.</summary>
    public string? ExportStatus { get; private set; }

    public bool ExportFailed { get; private set; }

    public string? Caption => string.IsNullOrWhiteSpace(File.Caption) ? null : File.Caption;

    public bool CanPreview => File.FromBot && File.ContentType.Equals("text/html", StringComparison.OrdinalIgnoreCase);

    public bool Previewing { get; private set; }

    public string PreviewLabel => Previewing ? "Loading" : "Preview";

    public string PreviewButtonName => Previewing ? $"Loading a preview of {File.Name}" : $"Preview {File.Name}";

    public string? PreviewAddress { get; private set; }

    public string? PreviewProblem { get; private set; }

    #endregion

    #region Public Methods

    public async Task Export(bool save)
    {
        // A second press before the buttons are redrawn as disabled would start a second export.
        if (Exporting || delivery is null)
        {
            return;
        }

        Exporting = true;
        ExportFailed = false;
        ExportStatus = null;
        Changed();

        try
        {
            if (!save)
            {
                await delivery.Share(File);
            }
            else if (await delivery.Save(File))
            {
                ExportStatus = $"Saved {File.Name}.";
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception) || exception is IOException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(exception, "A conversation file could not be handed to the device");
            ExportFailed = true;
            ExportStatus = save ? $"Could not save {File.Name}." : $"Could not share {File.Name}.";
        }
        finally
        {
            Exporting = false;
            Changed();
        }
    }

    // The button stays focusable while it reads, so focus is not lost; a second press meanwhile does nothing.
    public async Task ShowAll()
    {
        if (Reading || ReadWhole is null)
        {
            return;
        }

        Reading = true;
        Problem = null;
        Changed();

        try
        {
            whole = await ReadWhole(File);
            focusWhole = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "The rest of a conversation file could not be read");
            Problem = $"Drover could not load the rest of {File.Name}. {Failures.Sentence(exception, "Check that the server is running.")}";
        }
        finally
        {
            Reading = false;
            Changed();
        }
    }

    /// <summary>True once, after the whole text is in: the button is gone, so the focus moves to the text it brought.</summary>
    public bool TakeFocusForText()
    {
        if (focusWhole)
        {
            focusWhole = false;
            return true;
        }

        return false;
    }

    public async Task Preview()
    {
        if (!CanPreview || Previewing || RequestPreview is null)
        {
            return;
        }

        var file = File;
        var generation = previewGeneration;
        Previewing = true;
        PreviewProblem = null;
        Changed();

        try
        {
            var response = await RequestPreview(file);
            if (generation != previewGeneration || File.Id != file.Id)
            {
                await RevokeGrant(file, response.Grant);
                return;
            }

            if (response.Address is { } address
                && Uri.TryCreate(address, UriKind.Absolute, out var parsed)
                && parsed.Scheme is "https"
                && !SharesAppSite(file.Href, parsed)
                && response.Grant is { Length: > 0 } grant)
            {
                PreviewAddress = address;
                previewGrant = grant;
            }
            else
            {
                await RevokeGrant(file, response.Grant);
                PreviewProblem = response.Problem ?? $"Drover could not preview {File.Name}. Download is still available.";
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception) || exception is ArgumentException)
        {
            logger.LogWarning(exception, "A conversation file preview could not be issued");
            if (generation == previewGeneration && File.Id == file.Id)
            {
                PreviewProblem = $"Drover could not preview {file.Name}. {Failures.Sentence(exception)} Download is still available.";
            }
        }
        finally
        {
            Previewing = false;
            Changed();
        }
    }

    public void RefusePreview()
    {
        PreviewProblem = "Preview is not available on this device. Save the file to view it.";
        Changed();
    }

    public async Task ClosePreview()
    {
        var file = File;
        var grant = previewGrant;
        previewGeneration++;
        PreviewAddress = null;
        previewGrant = null;
        Changed();

        await RevokeGrant(file, grant);
    }

    #endregion

    #region Fixed Words

    public const string Save = "Save";

    public const string Share = "Share";

    public const string Download = "Download";

    public const string PreviewFile = "Preview";

    #endregion

    #region Private Methods

    private static bool SharesAppSite(string fileHref, Uri preview) =>
        Uri.TryCreate(fileHref, UriKind.Absolute, out var app)
        && string.Equals(Site(app), Site(preview), StringComparison.OrdinalIgnoreCase);

    private static string Site(Uri address)
    {
        var host = address.IdnHost.TrimEnd('.');
        return host.Split('.') is [.., var secondLast, var last] && !System.Net.IPAddress.TryParse(host, out _)
            ? $"{secondLast}.{last}"
            : host;
    }

    private async Task RevokeGrant(SharedFile file, string? grant)
    {
        if (grant is null || RevokePreview is null)
        {
            return;
        }

        try
        {
            await RevokePreview(file, grant);
        }
        catch (Exception exception) when (Failures.IsReportable(exception) || exception is ArgumentException)
        {
            logger.LogWarning(exception, "An HTML preview grant could not be revoked");
        }
    }

    private void Changed() => OnPropertyChanged(string.Empty);

    #endregion
}
