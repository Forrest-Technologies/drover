namespace Drover.Presentation.ViewModels;

/// <summary>
/// The first step of importing an extension: the file's text, from a file the person chose or pasted. A file and
/// pasted text go the same way from here.
/// </summary>
public sealed partial class ImportExtensionViewModel(ILogger logger) : ObservableObject
{
    #region Private Fields

    private const long MaxFileBytes = 256 * 1024;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRead))]
    private string text = string.Empty;

    [ObservableProperty]
    private string? fileProblem;

    #endregion

    #region Properties

    public static string Title => "Import an extension";

    public static string FileLabel => "Extension file";

    public static string PasteLabel => "Or paste its text";

    public static string PasteHint => "Nothing is added by reading it. You see what it holds first.";

    public static string CancelLabel => "Cancel";

    /// <summary>There is nothing to read until there is some text.</summary>
    public bool CanRead => !string.IsNullOrWhiteSpace(Text);

    #endregion

    #region Public Methods

    /// <summary>
    /// Reads the chosen file into the text, so a file and pasted text go the same way from here. A file the
    /// platform reports as too large is not opened.
    /// </summary>
    public async Task Choose(long size, Func<Task<string>> readAll)
    {
        FileProblem = null;

        if (size > MaxFileBytes)
        {
            FileProblem = "This file is larger than an extension can be (256 KB).";
            return;
        }

        try
        {
            Text = await readAll();
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "The extension file the person chose could not be read");
            FileProblem = "The file could not be read. Choose it again, or paste its text.";
        }
    }

    #endregion
}
