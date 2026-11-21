namespace Drover.Presentation.ViewModels;

/// <summary>
/// One project: its name, its instructions, its files, and the conversations a person can open or move.
/// Instructions and files are the project's own. This screen does not change a bot, a model, or a grant.
/// </summary>
public sealed partial class ProjectViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    ILogger<ProjectViewModel> logger,
    IFileDelivery? delivery = null) : ObservableObject
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

    public const string ChoosingFiles = "Choosing files.";

    public const string ChooseFailed = "Could not choose a file. Try again.";

    public const string OwnConversationsOnly = "Only your own conversations can move out of this project.";

    public const string RemovingFile = "Removing the file.";

    public const string Downloading = "Saving the file.";

    public const string MovingIn = "Moving the conversation in.";

    public const string MovingOut = "Moving the conversation out.";

    public const string Starting = "Starting the conversation.";

    private HashSet<Guid> ownedProjectThreads = [];

    private int loadGeneration;

    private long? maxFileBytes;

    private Guid draftProjectId;

    private bool nameEdited;

    private bool instructionsEdited;

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
    private string draftName = string.Empty;

    [ObservableProperty]
    private string draftInstructions = string.Empty;

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

    /// <summary>True on a device, where a file is saved through the system instead of launched in a browser.</summary>
    public bool Delivers => delivery is not null;

    public string TooLarge => TooLargeFor(maxFileBytes);

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
        ownedProjectThreads.Clear();
        LoadProblem = null;
    }

    /// <summary>Runs when the screen opens at a project's address.</summary>
    public async Task Open(Guid projectId)
    {
        if (draftProjectId != projectId)
        {
            ownedProjectThreads.Clear();
            draftProjectId = projectId;
            nameEdited = false;
            instructionsEdited = false;
            DraftName = string.Empty;
            DraftInstructions = string.Empty;
            Project = null;
            ConfirmingRemove = false;
            Problem = null;
        }

        ProjectId = projectId;
        LoadProblem = null;
        Missing = false;

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
                "A request to the server failed on {Screen} for project {ProjectId}: {FailureKind} ({StatusCode})",
                nameof(ProjectViewModel),
                ProjectId,
                exception.GetType().Name,
                exception is DroverApiException apiException ? (int)apiException.StatusCode : null);
            LoadProblem = Failures.Sentence(exception);
        }
    }

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

            var foundRules = await api.GetFileRules(SpaceId);
            if (!Current(ticket))
            {
                return;
            }

            var foundBots = await api.ListBots(SpaceId);
            if (!Current(ticket))
            {
                return;
            }

            var foundOwned = await api.ListOwnedThreads(SpaceId);
            if (!Current(ticket))
            {
                return;
            }

            Project = found;
            maxFileBytes = foundRules.MaxFileBytes;
            if (!nameEdited)
            {
                DraftName = found.Name;
            }

            if (!instructionsEdited)
            {
                DraftInstructions = found.Instructions ?? string.Empty;
            }

            Threads = foundThreads;
            Files = foundFiles;
            Bots = foundBots;
            Movable = [.. foundOwned.Where(thread => thread.ProjectId != projectId)];
            ownedProjectThreads =
                [.. foundOwned.Where(thread => thread.ProjectId == projectId).Select(thread => thread.Id)];
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            if (!Current(ticket))
            {
                return;
            }

            logger.LogWarning(
                "A request to the server failed on {Screen} for project {ProjectId}: {FailureKind} ({StatusCode})",
                nameof(ProjectViewModel),
                ProjectId,
                exception.GetType().Name,
                exception is DroverApiException apiException ? (int)apiException.StatusCode : null);
            Report(Failures.Sentence(exception));
        }
    }

    /// <summary>Drops a load that is still in flight, so its answer cannot replace a later one.</summary>
    public void Cancel()
    {
        loadGeneration++;
        Working = null;
    }

    public void UpdateDraftName(string value)
    {
        DraftName = value;
        nameEdited = true;
    }

    public void UpdateDraftInstructions(string value)
    {
        DraftInstructions = value;
        instructionsEdited = true;
    }

    public Task Save() => Save(DraftName, DraftInstructions);

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

        var submittedDraftName = DraftName;
        var submittedDraftInstructions = DraftInstructions;
        await Run(
            Saving,
            async () =>
            {
                await api.UpdateProject(SpaceId, ProjectId, new(name.Trim(), instructions ?? string.Empty));
                nameEdited = DraftName != submittedDraftName;
                instructionsEdited = DraftInstructions != submittedDraftInstructions;
            });
    }

    public void PrepareRemove() => ConfirmingRemove = true;

    public void CancelRemove() => ConfirmingRemove = false;

    /// <summary>Removes the project after the person confirms. Conversations stay, outside the project.</summary>
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
            logger.LogWarning(
                "A request to the server failed on {Screen} for project {ProjectId}: {FailureKind} ({StatusCode})",
                nameof(ProjectViewModel),
                ProjectId,
                exception.GetType().Name,
                exception is DroverApiException apiException ? (int)apiException.StatusCode : null);
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Working = null;
        }
    }

    /// <summary>Opens the device chooser and reports its failures on the project screen.</summary>
    public async Task ChooseFiles(Func<Task<IReadOnlyList<IPickedFile>>> choose)
    {
        if (!CanManage)
        {
            Problem = MemberNote;
            return;
        }

        Working = ChoosingFiles;
        try
        {
            var selected = await ChoosePickedFiles(choose);
            await UploadFiles(selected);
        }
        finally
        {
            Working = null;
        }
    }

    /// <summary>Uploads the chosen files and keeps every problem visible after the batch completes.</summary>
    public async Task UploadFiles(IReadOnlyList<IPickedFile> selected)
    {
        if (!CanManage)
        {
            Problem = MemberNote;
            return;
        }

        if (selected.Count == 0)
        {
            return;
        }

        List<string> problems = [];
        foreach (var file in selected)
        {
            var uploaded = await Upload(file);
            if (Problem is { } problem)
            {
                problems.Add(uploaded ? problem : $"{file.Name}: {problem}");
            }
        }

        Problem = problems.Count > 0 ? string.Join("\n", problems) : null;
    }

    /// <summary>Uploads one shared file and returns whether the server accepted it.</summary>
    public async Task<bool> Upload(IPickedFile file)
    {
        if (!CanManage)
        {
            Problem = MemberNote;
            return false;
        }

        Working = Uploading;
        Problem = null;
        var uploaded = false;

        try
        {
            var limit = maxFileBytes ?? (await api.GetFileRules(SpaceId)).MaxFileBytes;
            maxFileBytes = limit;

            if (file.Size > limit)
            {
                Problem = TooLargeFor(limit);
                return false;
            }

            await using var content = await ReadPickedFile(file, limit);
            if (content is null)
            {
                return false;
            }

            if (file.Size > 0)
            {
                await api.UploadProjectFile(SpaceId, ProjectId, file.Name, content, file.Size);
            }
            else
            {
                await api.UploadProjectFileWithUnverifiedSize(SpaceId, ProjectId, file.Name, content, limit);
            }

            uploaded = true;
            await Load();
            if (Problem is not null || LoadProblem is not null)
            {
                Problem = UploadedWithoutRefresh(file.Name);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception)
            || exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "A request to the server failed on {Screen} for project {ProjectId}: {FailureKind} ({StatusCode})",
                nameof(ProjectViewModel),
                ProjectId,
                exception.GetType().Name,
                exception is DroverApiException apiException ? (int)apiException.StatusCode : null);
            Problem = uploaded
                ? UploadedWithoutRefresh(file.Name)
                : Failures.IsReportable(exception)
                    ? Failures.Sentence(exception)
                    : $"Could not read {file.Name}.";
        }
        finally
        {
            Working = null;
        }

        return uploaded;
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

    /// <summary>Saves a shared project file through platform file delivery on desktop and Android.</summary>
    public async Task Download(ProjectFileResponse file)
    {
        if (delivery is null)
        {
            return;
        }

        Working = Downloading;
        Problem = null;

        try
        {
            var shared = new SharedFile(
                file.File.Id,
                file.File.Name,
                file.File.ContentType,
                file.File.Size,
                DownloadAddress(file.File.Id),
                FromBot: false,
                file.UploadedAt);
            await delivery.Save(shared);
        }
        catch (Exception exception) when (Failures.IsReportable(exception)
            || exception is IOException or UnauthorizedAccessException
                or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(
                "A request to the server failed on {Screen} for project {ProjectId}: {FailureKind} ({StatusCode})",
                nameof(ProjectViewModel),
                ProjectId,
                exception.GetType().Name,
                exception is DroverApiException apiException ? (int)apiException.StatusCode : null);
            Problem = Failures.IsReportable(exception) ? Failures.Sentence(exception) : $"Could not save {file.File.Name}.";
        }
        finally
        {
            Working = null;
        }
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
            logger.LogWarning(
                "A request to the server failed on {Screen} for project {ProjectId}: {FailureKind} ({StatusCode})",
                nameof(ProjectViewModel),
                ProjectId,
                exception.GetType().Name,
                exception is DroverApiException apiException ? (int)apiException.StatusCode : null);
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Working = null;
        }
    }

    public Task MoveIn(Guid threadId) => Move(threadId, ProjectId, MovingIn);

    public bool CanMoveOut(Guid threadId) => ownedProjectThreads.Contains(threadId);

    public Task MoveOut(Guid threadId)
    {
        if (!CanMoveOut(threadId))
        {
            Problem = OwnConversationsOnly;
            return Task.CompletedTask;
        }

        return Move(threadId, null, MovingOut);
    }

    public string DownloadAddress(Guid fileId) => api.ProjectFileAddress(SpaceId, ProjectId, fileId, download: true);

    public string ThreadLabel(ThreadSummary thread)
    {
        var bot = Bots.FirstOrDefault(candidate => candidate.Id == thread.BotId);
        var who = bot?.Name ?? "a bot";
        return string.IsNullOrWhiteSpace(thread.Title) ? $"Conversation with {who}" : thread.Title;
    }

    public static string TooLargeFor(long? limit) =>
        limit is { } bytes
            ? $"That file is larger than {Words.Size(bytes)}. Drover did not upload it."
            : "That file is larger than the server allows. Drover did not upload it.";

    public static string FileLabel(ProjectFileResponse file) => $"{file.File.Name}, {Words.Size(file.File.Size)}";

    public static string StartLabel(BotSummary bot) => $"Start with {bot.Name}";

    public static string DownloadLabel(ProjectFileResponse file) => $"Download {file.File.Name}";

    public static string RemoveFileLabel(ProjectFileResponse file) => $"Remove {file.File.Name}";

    public static string MoveInLabel(string threadLabel) => $"Move {threadLabel} into this project";

    public static string MoveOutLabel(string threadLabel) => $"Move {threadLabel} out of this project";

    #endregion

    #region Private Methods

    private async Task<IReadOnlyList<IPickedFile>> ChoosePickedFiles(
        Func<Task<IReadOnlyList<IPickedFile>>> choose)
    {
        try
        {
            return await choose();
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("File selection was canceled on {Screen}", nameof(ProjectViewModel));
            return [];
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning(
                "The file chooser failed on {Screen}: {FailureKind}",
                nameof(ProjectViewModel),
                exception.GetType().Name);
            Problem = ChooseFailed;
            return [];
        }
    }

    private async Task<Stream?> ReadPickedFile(IPickedFile file, long limit)
    {
        try
        {
            return await file.OpenRead(limit, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning(
                "A picked file could not be read on {Screen}: {FailureKind}",
                nameof(ProjectViewModel),
                exception.GetType().Name);
            Problem = $"Could not read {file.Name}.";
            return null;
        }
    }

    private static string UploadedWithoutRefresh(string name) =>
        $"Uploaded {name}. Reopen this project to refresh its files.";

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
                ownedProjectThreads.Clear();
                Missing = true;
            }

            logger.LogInformation(
                "Project {ProjectId} was not found in this space ({StatusCode})",
                projectId,
                (int)exception.StatusCode);
            return null;
        }
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
            logger.LogWarning(
                "A request to the server failed on {Screen} for project {ProjectId}: {FailureKind} ({StatusCode})",
                nameof(ProjectViewModel),
                ProjectId,
                exception.GetType().Name,
                exception is DroverApiException apiException ? (int)apiException.StatusCode : null);
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
            logger.LogWarning(
                "A request to the server failed on {Screen} for project {ProjectId}: {FailureKind} ({StatusCode})",
                nameof(ProjectViewModel),
                ProjectId,
                exception.GetType().Name,
                exception is DroverApiException apiException ? (int)apiException.StatusCode : null);
            Problem = Failures.Sentence(exception);
        }
        finally
        {
            Working = null;
        }
    }

    #endregion
}
