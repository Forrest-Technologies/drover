namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// The files a person and a bot have exchanged, beside the conversation, newest first. Its job is finding one again.
/// The one filter appears only when files came from both sides.
/// </summary>
public sealed class FilesPanelViewModel : ObservableObject
{
    #region Private Fields

    private FilesFrom from = FilesFrom.Everyone;

    #endregion

    #region Properties

    public FilesPanelState State { get; set; }

    public string BotName { get; set; } = string.Empty;

    /// <summary>The files, newest first.</summary>
    public IReadOnlyList<SharedFile> Files { get; set; } = [];

    public string? Problem { get; set; }

    /// <summary>Reads a text file's whole content for "Show all".</summary>
    public Func<SharedFile, Task<FileText>>? ReadWhole { get; set; }

    /// <summary>Whose files are listed.</summary>
    public FilesFrom From
    {
        get => from;
        set
        {
            if (from != value)
            {
                from = value;
                OnPropertyChanged(string.Empty);
            }
        }
    }

    public string FailedCause => Problem ?? "The server did not answer.";

    public string EmptyHint => $"No files yet. Files you attach and files {BotName} shares are listed here.";

    public bool ShowsFilter => Files.Any(file => file.FromBot) && Files.Any(file => !file.FromBot);

    public IReadOnlyList<SegmentOption<FilesFrom>> Sides =>
    [
        new(FilesFrom.Everyone, "All"),
        new(FilesFrom.Bot, $"From {BotName}"),
        new(FilesFrom.You, "From you"),
    ];

    public string ListName => $"Files exchanged with {BotName}";

    public IEnumerable<SharedFile> Shown => From switch
    {
        FilesFrom.Bot => Files.Where(file => file.FromBot),
        FilesFrom.You => Files.Where(file => !file.FromBot),
        _ => Files,
    };

    #endregion

    #region Public Methods

    /// <summary>Who a file came from and when: the line under its card.</summary>
    public string Origin(SharedFile file) => $"{FileWords.From(file, BotName)}, {ScheduleWords.Moment(file.When)}";

    public static string OpenMomentName(SharedFile file) => $"Open that moment: {file.Name}";

    #endregion

    #region Fixed Words

    public const string Loading = "Loading the files.";

    public const string FailedTitle = "Drover could not load the files";

    public const string TryAgain = "Try again";

    public const string FilterName = "Whose files";

    public const string OpenMoment = "Open that moment";

    #endregion
}
