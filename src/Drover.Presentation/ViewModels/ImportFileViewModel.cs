namespace Drover.Presentation.ViewModels;

/// <summary>The words of an import's first step, which differ by what is imported: a bot or an extension.</summary>
public sealed record ImportWords(string Title, string FileLabel, string TooLarge);

/// <summary>
/// The first step of importing a file: its text, from a file the person chose or pasted. A file and pasted text go
/// the same way from here.
/// </summary>
public sealed partial class ImportFileViewModel(ILogger logger, ImportWords words) : ObservableObject
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

    public static string PasteLabel => "Or paste its text";

    public static string PasteHint => "Nothing is added by reading it. You see what it holds first.";

    public static string CancelLabel => "Cancel";

    public string Title => words.Title;

    public string FileLabel => words.FileLabel;

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
            FileProblem = words.TooLarge;
            return;
        }

        try
        {
            Text = await readAll();
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "The file the person chose to import could not be read");
            FileProblem = "The file could not be read. Choose it again, or paste its text.";
        }
    }

    #endregion
}

/// <summary>
/// An import in two steps, as the form that shows it needs it: choose the file, then read what it adds before saying
/// yes. The bots screen and the extensions screen each run one.
/// </summary>
public interface IImportFlow : System.ComponentModel.INotifyPropertyChanged
{
    ImportFileViewModel Form { get; }

    /// <summary>What the server read from the file, to review. Null while the file is still being chosen.</summary>
    object? Reviewed { get; }

    string ImportTitle { get; }

    string ReadLabel { get; }

    string ImportLabel { get; }

    bool ImportBusy { get; }

    string? ImportProblem { get; }

    bool CanImport { get; }

    IAsyncRelayCommand ReadFormCommand { get; }

    IAsyncRelayCommand ImportCommand { get; }

    IRelayCommand StopImportingCommand { get; }
}
