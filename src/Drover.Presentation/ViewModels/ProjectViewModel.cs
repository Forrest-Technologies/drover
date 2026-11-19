namespace Drover.Presentation.ViewModels;

/// <summary>
/// One project: its name, its instructions, its files, and the conversations a person can open or move.
/// Instructions and files are the project's own. This screen does not change a bot, a model, or a grant.
/// </summary>
public sealed partial class ProjectViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ILogger<ProjectViewModel> logger) : ObservableObject
{
    #region Private Fields

    public const string Title = "Project · Drover";

    public const string Loading = "Loading the project.";

    public const string LoadFailedTitle = "Drover could not load the project";

    public const string TryAgain = "Try again";

    public const string MissingTitle = "This project is not here";

    public const string MissingCause = "It may have been removed, or it is not in this space.";

    public const string MissingAction = "Back to projects";

    public const string MemberNote =
        "Owners and admins change a project's name, instructions, and files. You can read them and use the conversations.";

    public const string NeedsName = "A project needs a name.";

    public const string RemoveQuestion = "Remove this project? Conversations stay. They leave the project.";

    public const string KeepProject = "Keep project";

    public const string RemoveProject = "Remove project";

    public const string NoFiles = "No shared files yet.";

    public const string NoConversations = "No conversation in this project yet.";

    public const string NoBot = "This space has no bot to talk to yet.";

    public const string NoMovable = "No other conversation of yours can move in.";

    public const string AddFile = "Add a file";

    public const string Saving = "Saving the project.";

    public const string Removing = "Removing the project.";

    public const string Uploading = "Uploading the file.";

    public const string RemovingFile = "Removing the file.";

    public const string MovingIn = "Moving the conversation in.";

    public const string MovingOut = "Moving the conversation out.";

    public const string Starting = "Starting the conversation.";

    /// <summary>How much of an unknown-size file this screen will read. The server still enforces its own limit.</summary>
    private const long OpenReadCap = 25 * 1024 * 1024;

    private int loadGeneration;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State), nameof(Name), nameof(Instructions))]
    private ProjectSummary? project;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    private bool missing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(State))]
    private string? loadProblem;

    [ObservableProperty]
    private string? problem;

    [ObservableProperty]
    private string? working;

    [ObservableProperty]
    private bool confirmingRemove;

    [ObservableProperty]
    private IReadOnlyList<ThreadSummary> threads = [];

    [ObservableProperty]
    private IReadOnlyList<ProjectFileResponse> files = [];

    [ObservableProperty]
    private IReadOnlyList<BotSummary> bots = [];

    [ObservableProperty]
    private IReadOnlyList<ThreadSummary> movable = [];

    #endregion

    #region Properties

    public ProjectScreenState State => (Missing, LoadProblem, Project) switch
    {
        (true, _, _) => ProjectScreenState.Missing,
        (_, { }, _) => ProjectScreenState.Failed,
        (_, _, null) => ProjectScreenState.Loading,
        _ => ProjectScreenState.Ready,
    };

    public string Name => Project?.Name ?? string.Empty;

    public string Instructions => Project?.Instructions ?? string.Empty;

    /// <summary>Bots a person can start a conversation with. A paused bot is left out; the server would refuse it.</summary>
    public IReadOnlyList<BotSummary> ActiveBots => [.. Bots.Where(bot => bot.Status is "active")];

    public static string TooLarge => $"That file is larger than {Words.Size(OpenReadCap)}. Drover did not upload it.";

    /// <summary>The server still checks the role. This only keeps a control a person cannot use from looking ready.</summary>
    public bool CanManage => session.Space?.Role is "owner" or "admin";

    private Guid ProjectId { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    #endregion

    #region Public Methods

    /// <summary>The address named no project. The screen says it is not here.</summary>
    public void Absent()
    {
        Project = null;
        Missing = true;
        LoadProblem = null;
    }

    /// <summary>Runs when the screen opens at a project's address.</summary>
    public async Task Open(Guid projectId)
    {
        ProjectId = projectId;
        if (await session.Load() is not null)
        {
            await Load();
        }
    }

    [RelayCommand]
    public async Task Load()
    {
        var ticket = ++loadGeneration;
        var projectId = ProjectId;
        LoadProblem = null;
        Missing = false;

        try
        {
            var found = await ReadProject(projectId, ticket);
            if (!Current(ticket) || found is null)
            {
                return;
            }

            var foundThreads = await api.ListProjectThreads(SpaceId, projectId);
            if (!Current(ticket))
            {
                return;
            }

            var foundFiles = await api.ListProjectFiles(SpaceId, projectId);
            if (!Current(ticket))
            {
                return;
            }

            var foundBots = await api.ListBots(SpaceId);
            if (!Current(ticket))
            {
                return;
            }

            var foundMovable = await ReadMovable(projectId, foundBots, ticket);
            if (!Current(ticket) || foundMovable is null)
            {
                return;
            }

            Project = found;
            Threads = foundThreads;
            Files = foundFiles;
            Bots = foundBots;
            Movable = foundMovable;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            if (!Current(ticket))
            {
                return;
            }

            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ProjectViewModel));
            Report(Failures.Sentence(exception));
        }
    }

    /// <summary>Drops a load that is still in flight, so its answer cannot replace a later one.</summary>
    public void Cancel()
    {
        loadGeneration++;
        Working = null;
    }

    /// <summary>Saves the name and instructions the person currently has in the fields.</summary>
    public async Task Save(string? name, string? instructions)
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

        await Run(Saving, () => api.UpdateProject(SpaceId, ProjectId, new(name.Trim(), instructions ?? string.Empty)));
    }

    public void PrepareRemove() => ConfirmingRemove = true;

    public void CancelRemove() => ConfirmingRemove = false;

    /// <summary>Removes the project after the person confirms. Conversations stay, outside the project.</summary>
    [RelayCommand]
    public async Task Remove()
    {
        if (!CanManage || !ConfirmingRemove)
        {
            return;
        }

        Working = Removing;
        Problem = null;

        try
        {
            await api.RemoveProject(SpaceId, ProjectId);
            navigator.NavigateTo(Addresses.Projects);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ProjectViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Working = null;
        }
    }

    /// <summary>Uploads one shared file. A file the screen can already see is too large is not sent.</summary>
    public async Task Upload(IPickedFile file)
    {
        if (!CanManage)
        {
            Problem = MemberNote;
            return;
        }

        if (file.Size > OpenReadCap)
        {
            Problem = TooLarge;
            return;
        }

        Working = Uploading;
        Problem = null;

        try
        {
            await using var content = await file.OpenRead(OpenReadCap, CancellationToken.None);
            if (file.Size > 0)
            {
                await api.UploadProjectFile(SpaceId, ProjectId, file.Name, content, file.Size);
            }
            else
            {
                await api.UploadProjectFileWithUnverifiedSize(SpaceId, ProjectId, file.Name, content, OpenReadCap);
            }

            await Load();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ProjectViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Working = null;
        }
    }

    public Task RemoveFile(Guid fileId)
    {
        if (!CanManage)
        {
            Problem = MemberNote;
            return Task.CompletedTask;
        }

        return Run(RemovingFile, () => api.RemoveProjectFile(SpaceId, ProjectId, fileId));
    }

    /// <summary>Starts a conversation with a bot inside this project, then opens it.</summary>
    public async Task Start(Guid botId)
    {
        Working = Starting;
        Problem = null;

        try
        {
            var thread = await api.CreateThread(SpaceId, botId, title: null, ProjectId);
            navigator.NavigateTo(Addresses.Conversation(thread.Id));
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ProjectViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Working = null;
        }
    }

    public Task MoveIn(Guid threadId) => Move(threadId, ProjectId, MovingIn);

    public Task MoveOut(Guid threadId) => Move(threadId, null, MovingOut);

    public string DownloadAddress(Guid fileId) => api.ProjectFileAddress(SpaceId, ProjectId, fileId, download: true);

    public string ThreadLabel(ThreadSummary thread)
    {
        var bot = Bots.FirstOrDefault(candidate => candidate.Id == thread.BotId);
        var who = bot?.Name ?? "a bot";
        return string.IsNullOrWhiteSpace(thread.Title) ? $"Conversation with {who}" : thread.Title;
    }

    public static string FileLabel(ProjectFileResponse file) => $"{file.File.Name}, {Words.Size(file.File.Size)}";

    public static string StartLabel(BotSummary bot) => $"Start with {bot.Name}";

    public static string DownloadLabel(ProjectFileResponse file) => $"Download {file.File.Name}";

    public static string RemoveFileLabel(ProjectFileResponse file) => $"Remove {file.File.Name}";

    public static string MoveInLabel(string threadLabel) => $"Move {threadLabel} into this project";

    public static string MoveOutLabel(string threadLabel) => $"Move {threadLabel} out of this project";

    #endregion

    #region Private Methods

    private bool Current(int ticket) => ticket == loadGeneration;

    private void Report(string sentence)
    {
        if (Project is null)
        {
            LoadProblem = sentence;
        }
        else
        {
            Problem = sentence;
        }
    }

    private async Task<ProjectSummary?> ReadProject(Guid projectId, int ticket)
    {
        try
        {
            return await api.GetProject(SpaceId, projectId);
        }
        catch (DroverApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            if (Current(ticket))
            {
                Project = null;
                Missing = true;
            }

            logger.LogInformation(exception, "Project {ProjectId} is not in this space", projectId);
            return null;
        }
    }

    private async Task<IReadOnlyList<ThreadSummary>?> ReadMovable(Guid projectId, IReadOnlyList<BotSummary> foundBots, int ticket)
    {
        IReadOnlyList<ThreadSummary> found = [];
        foreach (var bot in foundBots)
        {
            var own = await api.ListThreads(SpaceId, bot.Id);
            if (!Current(ticket))
            {
                return null;
            }

            found = [.. found, .. own.Where(thread => thread.ProjectId != projectId)];
        }

        return found;
    }

    private async Task Run(string progress, Func<Task> work)
    {
        Working = progress;
        Problem = null;

        try
        {
            await work();
            await Load();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ProjectViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Working = null;
        }
    }

    private async Task Move(Guid threadId, Guid? projectId, string progress)
    {
        Working = progress;
        Problem = null;

        try
        {
            await api.SetThreadProject(SpaceId, threadId, projectId);
            await Load();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ProjectViewModel));
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Working = null;
        }
    }

    #endregion
}
