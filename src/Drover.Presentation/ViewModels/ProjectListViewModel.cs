namespace Drover.Presentation.ViewModels;

/// <summary>
/// The projects of the space. An owner or admin makes one. Anyone in the space opens one.
/// </summary>
public sealed partial class ProjectListViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ILogger<ProjectListViewModel> logger) : ObservableObject
{
    #region Private Fields

    public const string Title = "Projects · Drover";

    public const string Heading = "Projects";

    public const string Loading = "Loading the projects.";

    public const string LoadFailedTitle = "Drover could not load the projects";

    public const string TryAgain = "Try again";

    public const string EmptyTitle = "No project yet";

    public const string EmptyCause = "A project holds instructions and files that every conversation in it can use.";

    public const string EmptyAction = "New project";

    public const string MemberEmptyCause = "An owner or admin of the space can create one.";

    public const string MemberEmptyAction = "Back to conversations";

    public const string MemberNote =
        "Owners and admins create projects. You can open any project and use its conversations.";

    public const string NeedsName = "Enter a name for the project.";

    public const string Making = "Creating the project.";

    private int loadGeneration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    private IReadOnlyList<ProjectSummary>? projects;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    private string? loadProblem;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    private string? working;

    [ObservableProperty]
    private string draftName = string.Empty;

    [ObservableProperty]
    private bool creating;

    #endregion

    #region Properties

    public ProjectListState State => (LoadProblem, Projects) switch
    {
        ({ }, _) => ProjectListState.Failed,
        (_, null) => ProjectListState.Loading,
        (_, { Count: 0 }) => ProjectListState.Empty,
        _ => ProjectListState.Listed,
    };

    /// <summary>The server still checks the role. This only keeps a control a person cannot use from looking ready.</summary>
    public bool CanManage => session.Space?.Role is "owner" or "admin";

    /// <summary>The header offers a new project only once the list has loaded and the role can create one.</summary>
    public bool ShowsNewProject => CanManage && State is ProjectListState.Listed;

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    /// <summary>Runs when the screen opens.</summary>
    public async Task Open()
    {
        DraftName = string.Empty;
        LoadProblem = null;
        Problem = null;
        Creating = false;

        try
        {
            if (await session.Load() is not null)
            {
                await Load();
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(
                "A request to the server failed on {Screen}: {FailureKind} ({StatusCode})",
                nameof(ProjectListViewModel),
                exception.GetType().Name,
                StatusCodeOf(exception));
            LoadProblem = Failures.Sentence(exception);
        }
    }

    public async Task Load()
    {
        var ticket = ++loadGeneration;
        LoadProblem = null;

        try
        {
            var found = await api.ListProjects(SpaceId);
            if (ticket != loadGeneration)
            {
                return;
            }

            Projects = found;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            if (ticket != loadGeneration)
            {
                return;
            }

            logger.LogWarning(
                "A request to the server failed on {Screen}: {FailureKind} ({StatusCode})",
                nameof(ProjectListViewModel),
                exception.GetType().Name,
                StatusCodeOf(exception));
            LoadProblem = Failures.Sentence(exception);
        }
    }

    /// <summary>The first line of a project's instructions, for the line under its name on a card.</summary>
    public static string Facts(ProjectSummary project) =>
        project.Instructions.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(string.Empty);

    /// <summary>Drops a load that is still in flight, so its answer cannot replace a later one.</summary>
    public void Cancel()
    {
        loadGeneration++;
        Creating = false;
    }

    public void UpdateDraftName(string value) => DraftName = value;

    /// <summary>Opens the dialog with an empty name and no old reason on it.</summary>
    public void StartCreating()
    {
        DraftName = string.Empty;
        Problem = null;
        Creating = true;
    }

    /// <summary>Closes the dialog and forgets what was typed and why the last try was refused.</summary>
    public void CancelCreating()
    {
        Creating = false;
        DraftName = string.Empty;
        Problem = null;
    }

    public Task Create() => Create(DraftName);

    /// <summary>Makes a project from the name the person typed, then opens it.</summary>
    public async Task Create(string? name)
    {
        if (!CanManage)
        {
            Problem = MemberNote;
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            Problem = NeedsName;
            return;
        }

        var ticket = loadGeneration;
        Working = Making;
        Problem = null;

        try
        {
            var project = await api.CreateProject(SpaceId, new(name.Trim()));
            if (ticket != loadGeneration)
            {
                return;
            }

            DraftName = string.Empty;
            Creating = false;
            navigator.NavigateTo(Addresses.Project(project.Id));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            if (ticket != loadGeneration)
            {
                return;
            }

            logger.LogWarning(
                "A request to the server failed on {Screen}: {FailureKind} ({StatusCode})",
                nameof(ProjectListViewModel),
                exception.GetType().Name,
                StatusCodeOf(exception));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            if (ticket == loadGeneration)
            {
                Working = null;
            }
        }
    }

    #endregion

    #region Private Methods

    private static int? StatusCodeOf(Exception exception) =>
        exception is DroverApiException apiException ? (int)apiException.StatusCode : null;

    #endregion
}
