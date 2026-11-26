namespace Drover.Presentation.ViewModels;

/// <summary>
/// The extensions of the space. Its job: bring in a file that adds things the space can pick up (a tool server,
/// themes, rules) after reading what it holds, and take one out again.
/// </summary>
public sealed partial class ExtensionsViewModel(DroverApi api, SessionState session, ILogger<ExtensionsViewModel> logger) : ObservableObject, IImportFlow
{
    #region Private Fields

    private static readonly ImportWords words = new("Import an extension", "Extension file", "This file is larger than an extension can be (256 KB).");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shows), nameof(ShowsActions))]
    private IReadOnlyList<ExtensionSummary>? extensions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportTitle), nameof(CanImport), nameof(Reviewed))]
    private ExtensionPreview? preview;

    [ObservableProperty]
    private string? packageText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shows))]
    private string? loadProblem;

    [ObservableProperty]
    private string? importProblem;

    [ObservableProperty]
    private string? notice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoticeKind))]
    private bool noticeIsProblem;

    [ObservableProperty]
    private bool importing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadLabel), nameof(ImportLabel))]
    private bool importBusy;

    [ObservableProperty]
    private ImportFileViewModel form = new(logger, words);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveLabel))]
    private ExtensionSummary? removing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RemoveLabel))]
    private bool removeBusy;

    [ObservableProperty]
    private string? removeProblem;

    #endregion

    #region Properties

    public static string Title => "Extensions";

    public static string PageTitle => "Extensions · Drover";

    public static string LoadingNote => "Loading the extensions.";

    public static string LoadFailedTitle => "Drover could not load the extensions";

    public static string EmptyTitle => "No extension yet";

    public static string EmptyCause =>
        "An extension is a file someone shares that adds to what this space can use: a tool server to connect, themes to choose, rules to give a bot. You read what it holds before anything is added, and it can give no bot a tool.";

    public static string EmptyForMember => "This space has no extensions yet. An owner or admin of the space can import one.";

    public static string ImportAction => "Import an extension";

    public static string RemoveAction => "Remove";

    public static string KeepLabel => "Keep it";

    public Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    /// <summary>The import button in the header: for people who can edit, once the list is known.</summary>
    public bool ShowsActions => CanEdit && Extensions is not null;

    public NoteKind NoticeKind => NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok;

    public object? Reviewed => Preview;

    public string ImportTitle => Preview is null ? Form.Title : $"Review {Preview.Name}";

    public string ReadLabel => ImportBusy ? "Reading the file" : "Read the file";

    public string ImportLabel => ImportBusy ? "Importing" : "Import";

    public string RemoveLabel => RemoveBusy ? "Removing" : Removing is null ? RemoveAction : $"Remove {Removing.Name}";

    /// <summary>A file that is new to the space can be imported.</summary>
    public bool CanImport => Preview is { AlreadyInstalled: false };

    /// <summary>What fills the screen under the header, in the order the screen decides it.</summary>
    public ExtensionsContent Shows =>
        LoadProblem is not null ? ExtensionsContent.LoadFailed
        : Extensions is null ? ExtensionsContent.Loading
        : Extensions.Count == 0 && CanEdit ? ExtensionsContent.Empty
        : Extensions.Count == 0 ? ExtensionsContent.EmptyForMember
        : ExtensionsContent.List;

    #endregion

    #region Public Methods

    public async Task Open()
    {
        if (await session.Load() is not null)
        {
            await Load();
        }
    }

    [RelayCommand]
    public async Task Load()
    {
        LoadProblem = null;

        try
        {
            Extensions = await api.ListExtensions(SpaceId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionsViewModel));
            LoadProblem = Failures.Sentence(exception);
        }
    }

    [RelayCommand]
    public void StartImporting()
    {
        Form = new(logger, words);
        Importing = true;
        Preview = null;
        PackageText = null;
        ImportProblem = null;
        Notice = null;
    }

    [RelayCommand]
    public void StopImporting() => Importing = false;

    /// <summary>Asks the server what the file would add. Nothing is added by reading it.</summary>
    public Task Read(string text) =>
        WhileImporting(async () =>
        {
            Preview = null;
            PackageText = text;
            Preview = await api.PreviewExtension(SpaceId, text);
        });

    /// <summary>Reads what the form holds, which is the text of the file the person chose or pasted.</summary>
    [RelayCommand]
    public Task ReadForm() => Read(Form.Text);

    [RelayCommand]
    public Task Import() =>
        WhileImporting(async () =>
        {
            var added = await api.ImportExtension(SpaceId, PackageText!);
            Extensions = [added, .. Extensions!];
            Importing = false;
            (Notice, NoticeIsProblem) = (ExtensionWords.Imported(added), false);
        });

    /// <summary>Shows an extension as it is after one of its steps.</summary>
    public void Replace(ExtensionSummary changed)
    {
        if (Extensions!.ToList().FindIndex(listed => listed.Id == changed.Id) is >= 0 and var index)
        {
            Extensions = [.. Extensions!.Take(index), changed, .. Extensions!.Skip(index + 1)];
        }
    }

    /// <summary>Asks whether the extension should go. Nothing is removed until the person says so.</summary>
    public void AskToRemove(ExtensionSummary extension)
    {
        (RemoveProblem, Notice) = (null, null);
        Removing = extension;
    }

    [RelayCommand]
    public void KeepIt() => Removing = null;

    [RelayCommand]
    public async Task Remove()
    {
        if (Removing is not { } going)
        {
            return;
        }

        RemoveBusy = true;
        RemoveProblem = null;

        try
        {
            await api.RemoveExtension(SpaceId, going.Id);
            Extensions = [.. Extensions!.Where(listed => listed.Id != going.Id)];
            Removing = null;
            (Notice, NoticeIsProblem) = (ExtensionWords.Removed(going), false);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionsViewModel));
            RemoveProblem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            RemoveBusy = false;
        }
    }

    #endregion

    #region Private Methods

    private async Task WhileImporting(Func<Task> work)
    {
        ImportBusy = true;
        ImportProblem = null;

        try
        {
            await work();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionsViewModel));
            ImportProblem = Failures.Sentence(exception, "Check that the server is running.");
        }
        finally
        {
            ImportBusy = false;
        }
    }

    #endregion
}

/// <summary>What the extensions screen shows under its header.</summary>
public enum ExtensionsContent
{
    Loading,
    LoadFailed,
    Nothing,
    Empty,
    EmptyForMember,
    List,
}
