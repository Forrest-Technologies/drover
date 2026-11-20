namespace Drover.Presentation.ViewModels;

/// <summary>
/// The extensions of the space. Its job: bring in a bot someone shared, after reading exactly what it will be told,
/// and turn it on.
/// </summary>
public sealed partial class ExtensionsViewModel(
    DroverApi api, SessionState session, ExtensionHandoff handoff, ILogger<ExtensionsViewModel> logger) : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shows), nameof(ShowsActions), nameof(ShowsImportButton))]
    private IReadOnlyList<ExtensionSummary>? extensions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportTitle), nameof(CanImport))]
    private ExtensionPreview? preview;

    [ObservableProperty]
    private string? packageText;

    [ObservableProperty]
    private Guid? busy;

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
    [NotifyPropertyChangedFor(nameof(Shows), nameof(ShowsImportButton))]
    private bool importing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReadLabel), nameof(ImportLabel))]
    private bool importBusy;

    [ObservableProperty]
    private ImportExtensionViewModel form = new(logger);

    #endregion

    #region Properties

    public static string Title => "Extensions";

    public static string PageTitle => "Extensions · Drover";

    public static string LoadingNote => "Loading the extensions.";

    public static string LoadFailedTitle => "Drover could not load the extensions";

    public static string EmptyTitle => "No extension yet";

    public static string EmptyCause =>
        "An extension is a file someone shares that adds a bot to this space, and can name a tool server for it. You read what it holds before anything is added, and the file can give the bot no tools.";

    public static string EmptyForMember => "This space has no extensions yet. An owner or admin of the space can import one.";

    public Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    /// <summary>The make and import buttons in the header: for people who can edit, once the list is known.</summary>
    public bool ShowsActions => CanEdit && Extensions is not null;

    public bool ShowsImportButton => Extensions is { Count: > 0 } && !Importing;

    public NoteKind NoticeKind => NoticeIsProblem ? NoteKind.Danger : NoteKind.Ok;

    public string ImportTitle => Preview is null ? ImportExtensionViewModel.Title : $"Review {Preview.Name}";

    public string ReadLabel => ImportBusy ? "Reading the file" : "Read the file";

    public string ImportLabel => ImportBusy ? "Importing" : "Import, paused";

    /// <summary>A file that is new, whose bot (if it has one) has a model to think with, can be imported.</summary>
    public bool CanImport => Preview is { AlreadyInstalled: false } shown && shown.Bot is null or { Model: not null };

    /// <summary>What fills the screen under the header, in the order the screen decides it.</summary>
    public ExtensionsContent Shows =>
        LoadProblem is not null ? ExtensionsContent.LoadFailed
        : Extensions is null ? ExtensionsContent.Loading
        : Extensions.Count == 0 && Importing ? ExtensionsContent.Nothing
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

            // A file handed over by the editor is reviewed here like any other, before anything is added.
            if (CanEdit && LoadProblem is null && handoff.Take() is { } handed)
            {
                StartImporting();
                Form.Text = handed;
                await Read(handed);
            }
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
        Form = new(logger);
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

    public static BadgeKind StatusKind(ExtensionSummary extension) => extension.BotStatus is "active" ? BadgeKind.Ok : BadgeKind.Neutral;

    public static string SetUpLabel(ExtensionSummary extension) => $"Set up {extension.BotName}";

    /// <summary>A bot that is paused can be turned on, by a person who can edit.</summary>
    public bool ShowsTurnOn(ExtensionSummary extension) => CanEdit && extension.BotStatus is "paused";

    public string TurnOnLabel(ExtensionSummary extension) => Busy == extension.Id ? "Turning on" : $"Turn on {extension.BotName}";

    public async Task TurnOn(ExtensionSummary extension)
    {
        Busy = extension.Id;
        Notice = null;

        try
        {
            var bot = await api.ResumeBot(SpaceId, extension.BotId!.Value);
            Replace(extension with { BotStatus = bot.Status });
            (Notice, NoticeIsProblem) = ($"{bot.Name} is on.", false);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ExtensionsViewModel));
            (Notice, NoticeIsProblem) = (Failures.Sentence(exception, "Check that the server is running."), true);
        }
        finally
        {
            Busy = null;
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
