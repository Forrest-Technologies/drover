namespace Drover.Ui.Pages.Bots;

/// <summary>
/// The bot editor. Its job: make one bot yours, its name, character, purpose, and instructions, then open only the
/// section you need. The definition is saved together; tools, rules, and schedules are saved as each is changed.
/// </summary>
public sealed partial class BotEditor(
    DroverApi api,
    SessionState session,
    NavigationManager navigation,
    ToastService toasts,
    ILogger<BotEditor> logger)
{
    #region Private Fields

    private const string Model = nameof(Model);

    private const string Tools = nameof(Tools);

    private const string Rules = nameof(Rules);

    private const string Memory = nameof(Memory);

    private const string Schedule = nameof(Schedule);

    private const string Sandbox = nameof(Sandbox);

    private const string Team = nameof(Team);

    private readonly HashSet<string> open = [];

    private BotDetail? saved;

    private BotDraft? draft;

    private BotDraft? baseline;

    private IReadOnlyList<ModelConnectionSummary> connections = [];

    private IReadOnlyList<ModelCatalogResponse>? models;

    private IReadOnlyList<ScreenReaderChoice>? readers;

    private IReadOnlyList<BotToolEntry>? tools;

    private IReadOnlyList<ToolConnectionSummary> toolConnections = [];

    private IReadOnlyList<BotScheduleResponse>? schedules;

    private IReadOnlyList<BotRuleResponse>? rules;

    private bool writtenPatterns = true;

    private BotSandboxOptions sandboxOptions = new(Available: true, OpenNetwork: true, MaxIdleMinutes: 0, Desktop: true);

    private BotSandboxStatus? sandboxStatus;

    private IReadOnlyList<BotSummary> otherBots = [];

    private Guid? shownBotId;

    private string? loadProblem;

    private string? saveProblem;

    private string? actionProblem;

    private string? leavingFor;

    private bool notPermitted;

    private bool missing;

    private bool saving;

    private bool justSaved;

    private bool editingHandle;

    private bool removing;

    private bool leaving;

    private bool leaveConfirmed;

    #endregion

    #region Properties

    [Parameter]
    public Guid BotId { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // A number field holding something that is not a number counts as a change, so it is said and not dropped.
    private bool Dirty => draft is not null && baseline is not null && (draft.DiffersFrom(baseline) || draft.FindProblem() is not null);

    // The character is drawn from the name, so it follows the name as it is typed.
    private string ShownName => string.IsNullOrWhiteSpace(draft!.Name) ? saved!.Name : draft.Name;

    private string? ConnectionName => connections.FirstOrDefault(connection => connection.Id == draft!.ModelConnectionId)?.Name;

    // Tools are hidden only when the catalog says the model cannot call them. An unknown model keeps them.
    private bool ModelCallsTools => models?.FirstOrDefault(entry => entry.Model == draft!.Model)?.Capabilities.ToolCalling ?? true;

    // Unknown while the catalog loads; a model the catalog does not list is treated as one that cannot see, as the server does.
    private bool? ModelSeesImages => models is null ? null : models.FirstOrDefault(entry => entry.Model == draft!.Model)?.Capabilities.Vision ?? false;

    private string? ScreenReaderName =>
        draft?.ScreenReader is { } reader
            ? readers?.FirstOrDefault(choice => choice.ConnectionId == reader.ModelConnectionId && choice.Model == reader.Model)?.DisplayName
            : null;

    private string SaveWords => (Dirty, justSaved) switch
    {
        (true, _) => "Changes not saved yet",
        (false, true) => "Saved",
        _ => "Nothing to save",
    };

    #endregion

    #region Overrides

    protected override async Task OnParametersSetAsync()
    {
        if (shownBotId != BotId && await session.Load() is not null)
        {
            shownBotId = BotId;
            await Load();
        }
    }

    #endregion

    #region Private Methods

    private bool IsOpen(string section) => open.Contains(section);

    private void Toggle(string section, bool opened)
    {
        if (opened)
        {
            open.Add(section);
        }
        else
        {
            open.Remove(section);
        }
    }

    private void Changed() => justSaved = false;

    private void Rename(string name)
    {
        draft!.Rename(name);
        justSaved = false;
    }

    private async Task Load()
    {
        (saved, draft, baseline, models, tools, schedules, rules, readers) = (null, null, null, null, null, null, null, null);
        (loadProblem, saveProblem, actionProblem, notPermitted, missing, justSaved, editingHandle) = (null, null, null, false, false, false, false);
        open.Clear();

        try
        {
            var bot = await api.GetBotDefinition(SpaceId, BotId);
            connections = await api.ListModelConnections(SpaceId);
            otherBots = [.. (await api.ListBots(SpaceId)).Where(other => other.Id != BotId)];
            Show(bot);
            models = await api.ListModels(SpaceId, bot.ModelConnectionId);
            toolConnections = await api.ListToolConnections(SpaceId);
            tools = await api.ListBotTools(SpaceId, BotId);
            schedules = await api.ListBotSchedules(SpaceId, BotId);
            writtenPatterns = (await api.GetBotRuleOptions(SpaceId, BotId)).WrittenPatterns;
            sandboxOptions = await api.GetBotSandboxOptions(SpaceId, BotId);
            rules = await api.ListBotRules(SpaceId, BotId);
            sandboxStatus = await ReadSandbox();
            readers = await ListReaders();
        }
        catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.Forbidden)
        {
            logger.LogInformation(refused, "The {Screen} screen is not open to this person", nameof(BotEditor));
            notPermitted = true;
        }
        catch (DroverApiException absent) when (absent.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(absent, "The {Screen} screen was asked for a bot that is not there", nameof(BotEditor));
            missing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotEditor));
            loadProblem = Failures.Sentence(exception);
        }
    }

    /// <summary>The sandbox's state for the Sandbox section. The editor works without it, so a failure only leaves it out.</summary>
    private async Task<BotSandboxStatus?> ReadSandbox()
    {
        try
        {
            return await api.GetBotSandbox(SpaceId, BotId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The sandbox state is left out of the {Screen} screen", nameof(BotEditor));
            return null;
        }
    }

    /// <summary>
    /// The space's models that can read a bot's screen: those the catalog of each connection lists as reading images.
    /// The editor works without them, so a failure only leaves the choice out.
    /// </summary>
    private async Task<IReadOnlyList<ScreenReaderChoice>?> ListReaders()
    {
        try
        {
            List<ScreenReaderChoice> found = [];

            foreach (var connection in connections.OrderBy(connection => connection.Name, StringComparer.OrdinalIgnoreCase))
            {
                var listed = connection.Id == saved!.ModelConnectionId && models is not null ? models : await api.ListModels(SpaceId, connection.Id);
                found.AddRange(listed
                    .Where(entry => entry.State is "available" && entry.Capabilities.Vision)
                    .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => new ScreenReaderChoice(connection.Id, connection.Name, entry.Model, entry.DisplayName)));
            }

            return found;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The models that read images are left out of the {Screen} screen", nameof(BotEditor));
            return null;
        }
    }

    private void Show(BotDetail bot)
    {
        saved = bot;
        draft = BotDraft.From(bot);
        baseline = BotDraft.From(bot);
    }

    private async Task ChooseConnection(Guid connectionId)
    {
        models = null;
        justSaved = false;
        var connection = connections.First(candidate => candidate.Id == connectionId);

        await Try(
            async () =>
            {
                models = await api.ListModels(SpaceId, connectionId);
                draft!.Model = connection.DefaultModel ?? models.FirstOrDefault(entry => entry.State is "available")?.Model ?? string.Empty;
                draft.KeepWhatModelSupports(models.FirstOrDefault(entry => entry.Model == draft.Model)?.Capabilities);
            },
            problem => saveProblem = problem);
        models ??= [];
    }

    private async Task Save()
    {
        if (!Dirty || saving)
        {
            return;
        }

        if (draft!.FindProblem() is { } unreadable)
        {
            saveProblem = unreadable;
            return;
        }

        saving = true;
        saveProblem = null;
        await Try(
            async () =>
            {
                var staged = draft.StagedGrants;
                Show(await api.UpdateBotDefinition(SpaceId, BotId, draft.ToRequest()));
                await ApplyGrants(staged);
                justSaved = true;
                editingHandle = false;
                toasts.Show($"{saved!.Name} is saved.");
            },
            problem => saveProblem = problem);
        saving = false;
    }

    /// <summary>
    /// The grants staged in the Sandbox section, set one by one through the tools API as the Tools section sets them.
    /// The server checks each against the parent's ceiling.
    /// </summary>
    private async Task ApplyGrants(IReadOnlyDictionary<Guid, string> staged)
    {
        foreach (var (toolId, grant) in staged)
        {
            var changed = await api.SetBotToolGrant(SpaceId, BotId, toolId, grant);
            tools = [.. (tools ?? []).Select(tool => tool.ToolId == changed.ToolId ? changed : tool)];
        }
    }

    private void Discard()
    {
        Show(saved!);
        saveProblem = null;
        editingHandle = false;
    }

    private Task Duplicate() =>
        Try(
            async () =>
            {
                var copy = await api.DuplicateBot(SpaceId, BotId);
                toasts.Show($"{copy.Name} was made from {saved!.Name}.");
                leaveConfirmed = true;
                navigation.NavigateTo(Addresses.Bot(copy.Id));
            },
            problem => actionProblem = problem);

    private Task Pause() => SetStatus(() => api.PauseBot(SpaceId, BotId), "is paused. It starts no run until it is resumed.");

    private Task Resume() => SetStatus(() => api.ResumeBot(SpaceId, BotId), "is running again.");

    // Only the status is taken from the answer, so what is being typed is kept.
    private Task SetStatus(Func<Task<BotDetail>> change, string outcome) =>
        Try(
            async () =>
            {
                saved = saved! with { Status = (await change()).Status };
                toasts.Show($"{saved.Name} {outcome}");
            },
            problem => actionProblem = problem);

    private async Task Remove()
    {
        removing = false;
        await Try(
            async () =>
            {
                await api.RemoveBot(SpaceId, BotId);
                toasts.Show($"{saved!.Name} was removed.");
                leaveConfirmed = true;
                navigation.NavigateTo(Addresses.Bots);
            },
            problem => actionProblem = problem);
    }

    private void ConfirmLeaving(LocationChangingContext context)
    {
        if (Dirty && !leaveConfirmed)
        {
            context.PreventNavigation();
            leavingFor = context.TargetLocation;
            leaving = true;
        }
    }

    private void Leave()
    {
        leaving = false;
        leaveConfirmed = true;
        navigation.NavigateTo(leavingFor ?? Addresses.Bots);
    }

    private async Task Try(Func<Task> work, Action<string> report)
    {
        actionProblem = null;

        try
        {
            await work();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotEditor));
            report(Failures.Sentence(exception));
        }
    }

    #endregion
}
