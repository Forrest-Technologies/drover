namespace Drover.Presentation.ViewModels;

/// <summary>
/// The bot editor. Its job: make one bot yours, its name, character, purpose, and instructions, then open only the
/// section you need. The definition is saved together; tools, rules, and schedules are saved as each is changed.
/// </summary>
/// <remarks>
/// Every change raises one notice that everything may have changed, as a render does on the web: the screen reads
/// what it shows again. The sections are parts of this view model, each with what its own part of the screen does.
/// </remarks>
public sealed partial class BotEditorViewModel : ObservableObject
{
    #region Private Fields

    public const string ModelSection = "Model";

    public const string ToolsSection = "Tools";

    public const string RulesSection = "Rules";

    public const string MemorySection = "Memory";

    public const string RoutinesSection = "Routines";

    public const string SandboxSection = "Sandbox";

    public const string TeamSection = "Team";

    public const string LimitsSection = "Limits";

    private readonly DroverApi api;

    private readonly SessionState session;

    private readonly Navigator navigator;

    private readonly ToastService toasts;

    private readonly ILogger<BotEditorViewModel> logger;

    private readonly HashSet<string> open = [];

    private BotDraft? baseline;

    private Guid? shownBotId;

    private string? leavingFor;

    // The bot an unsaved new bot goes beneath, or null for the top of the crew.
    private Guid? newParentId;

    // The bot an unsaved new bot became, so saving the same draft again does not make a second.
    private Guid? madeBotId;

    // Set when both steps are done and the editor is going to the bot, so a second Save on the same draft does nothing.
    private bool madeAndOpened;

    // The id the new bot will have, chosen once for this draft and sent with every create of it. If the answer to a
    // create is lost, sending it again with the same id returns the bot the server made and never a second one,
    // whatever was edited in the draft in between. A create the server refused made nothing, so the next try chooses a
    // new id.
    private Guid? createRequestId;

    private bool leaveConfirmed;

    private bool closed;

    #endregion

    #region Constructors

    public BotEditorViewModel(DroverApi api, SessionState session, Navigator navigator, ToastService toasts, ILogger<BotEditorViewModel> logger)
    {
        (this.api, this.session, this.navigator, this.toasts, this.logger) = (api, session, navigator, toasts, logger);
        ModelPart = new(this);
        ToolsPart = new(this, api, logger);
        RulesPart = new(this, api, logger);
        RoutinesPart = new(this, api, session, logger);
        SandboxPart = new(this);
    }

    #endregion

    #region Properties

    public static string[] Sections { get; } =
        [ModelSection, ToolsSection, RulesSection, MemorySection, RoutinesSection, SandboxSection, TeamSection, LimitsSection];

    public Guid BotId { get; private set; }

    public BotDetail? Saved { get; private set; }

    /// <summary>What the person is typing. Null until the bot is read.</summary>
    public BotDraft? Draft { get; private set; }

    public IReadOnlyList<ModelConnectionSummary> Connections { get; private set; } = [];

    /// <summary>The models the chosen connection lists, or null while they load.</summary>
    public IReadOnlyList<ModelCatalogResponse>? Models { get; private set; }

    public IReadOnlyList<ScreenReaderChoice>? Readers { get; private set; }

    public IReadOnlyList<BotToolEntry>? Tools { get; internal set; }

    public IReadOnlyList<ToolConnectionSummary> ToolConnections { get; private set; } = [];

    public IReadOnlyList<BotScheduleResponse>? Schedules { get; internal set; }

    public IReadOnlyList<BotRuleResponse>? Rules { get; internal set; }

    public bool WrittenPatterns { get; private set; } = true;

    public BotSandboxOptions SandboxOptions { get; private set; } = new(Available: true, OpenNetwork: true, MaxIdleMinutes: 0, Desktop: true);

    public BotRunLimitOptions? RunLimitOptions { get; private set; }

    public BotSandboxStatus? SandboxStatus { get; private set; }

    public IReadOnlyList<BotSummary> OtherBots { get; private set; } = [];

    public string? LoadProblem { get; private set; }

    public string? SaveProblem { get; private set; }

    public string? ActionProblem { get; private set; }

    public bool NotPermitted { get; private set; }

    public bool Missing { get; private set; }

    public bool Saving { get; private set; }

    public bool JustSaved { get; private set; }

    public bool EditingHandle { get; private set; }

    /// <summary>True while the editor holds a bot that is not made yet. Nothing is stored until it is saved.</summary>
    public bool IsNew { get; private set; }

    /// <summary>True once a bot that was a draft has been made, while the rest of its settings are not saved yet.</summary>
    public bool IsMade => madeBotId is not null;

    /// <summary>Whether the dialog that asks about removing the bot is open.</summary>
    public bool Removing { get; private set; }

    /// <summary>Whether the dialog that asks about leaving without saving is open.</summary>
    public bool Leaving { get; private set; }

    public ModelSectionViewModel ModelPart { get; }

    public ToolsSectionViewModel ToolsPart { get; }

    public RulesSectionViewModel RulesPart { get; }

    public RoutinesSectionViewModel RoutinesPart { get; }

    public SandboxSectionViewModel SandboxPart { get; }

    public BotEditorState State => this switch
    {
        { LoadProblem: not null } => BotEditorState.Failed,
        { NotPermitted: true } => BotEditorState.NotPermitted,
        { Missing: true } => BotEditorState.Missing,
        { Saved: null } or { Draft: null } => BotEditorState.Loading,
        _ => BotEditorState.Shown,
    };

    public string Title => (Saved, IsNew) switch
    {
        (null, _) => "Bot · Drover",
        (_, true) => "New bot · Drover",
        _ => $"{Saved.Name} · Drover",
    };

    public static string Back => "Back to bots";

    public const string NewBotName = "New bot";

    public const string MadeButNotSet = "The bot was made, but its settings are not saved yet.";

    public static string NoModelCause => "A bot needs a model to think with, and this space has none yet. Connect a model first.";

    public static string LoadFailedTitle => "Drover could not load this bot";

    public static string TryAgain => "Try again";

    public static string NotPermittedTitle => "You cannot change this bot";

    public static string NotPermittedCause => "Owners and admins of this space change its bots. You can still talk to them.";

    public static string NotPermittedAction => "Back to the crew";

    public static string MissingTitle => "This bot is not here";

    public static string MissingCause => "It was removed, or it belongs to another space.";

    public static string MissingAction => "See the bots";

    public static string NotPermittedAddress => Addresses.Home;

    public static string MissingAddress => Addresses.Bots;

    public static string Loading => "Loading the bot.";

    public static string NameLabel => "Name";

    public static string HandleLabel => "Handle";

    public static string ChangeHandleLabel => "Change the handle";

    public static string PurposeLabel => "What this bot is for";

    public static string InstructionsLabel => "Instructions";

    public static string MenuLabel => "More";

    public static string DuplicateLabel => "Duplicate";

    public static string MakeExtensionLabel => "Make an extension";

    public static string PauseLabel => "Pause";

    public static string ResumeLabel => "Resume";

    public static string RemoveLabel => "Remove";

    public static string DiscardLabel => "Discard";

    public static string KeepBot => "Keep bot";

    public static string RemoveKeepRest => "Remove it, keep the rest";

    public static string RemoveBot => "Remove bot";

    public static string StayHere => "Stay here";

    public static string LeaveLabel => "Leave";

    public string Heading => IsNew ? "New bot" : $"Edit {Saved?.Name}";

    public static string HandleHint => "Lower-case letters, digits, and hyphens. Other bots and people address it by this.";

    public static string PurposeHint => "Other bots read this to decide when to hand it work.";

    public static string InstructionsHint => "What it does and how it answers. The model reads this before every conversation.";

    public bool IsPaused => Saved?.Status is "paused";

    public static string PausedBadge => "Paused";

    public string HandleShown => $"@{Draft?.Handle}";

    // The character is drawn from the name, so it follows the name as it is typed.
    public string ShownName => string.IsNullOrWhiteSpace(Draft!.Name) ? Saved!.Name : Draft.Name;

    public int Hue => BotIdentity.Hue(Saved!.Id);

    // A number field holding something that is not a number counts as a change, so it is said and not dropped.
    // A bot that is not made yet is always something to save: leaving it drops it.
    public bool Dirty => Draft is not null && baseline is not null && (IsNew || Draft.DiffersFrom(baseline) || Draft.FindProblem() is not null);

    public string SaveWords => (Dirty, JustSaved) switch
    {
        (true, _) when IsMade => "Made, but its settings are not saved yet",
        (true, _) when IsNew => "Not made until you save",
        (true, _) => "Changes not saved yet",
        (false, true) => "Saved",
        _ => "Nothing to save",
    };

    /// <summary>The bar shows when there is something to act on.</summary>
    public bool ShowsSaveBar => Dirty || SaveProblem is not null;

    public bool CanSave => Dirty && !Saving;

    public string SaveLabel => (Saving, IsNew, IsMade) switch
    {
        (true, true, false) => "Making",
        (true, _, _) => "Saving",
        (false, true, false) => "Make bot",
        _ => "Save changes",
    };

    public string? ConnectionName => Connections.FirstOrDefault(connection => connection.Id == Draft!.ModelConnectionId)?.Name;

    // Tools are hidden only when the catalog says the model cannot call them. An unknown model keeps them.
    public bool ModelCallsTools => Models?.FirstOrDefault(entry => entry.Model == Draft!.Model)?.Capabilities.ToolCalling ?? true;

    // Unknown while the catalog loads; a model the catalog does not list is treated as one that cannot see, as the server does.
    public bool? ModelSeesImages => Models is null ? null : Models.FirstOrDefault(entry => entry.Model == Draft!.Model)?.Capabilities.Vision ?? false;

    public string? ScreenReaderName =>
        Draft?.ScreenReader is { } reader
            ? Readers?.FirstOrDefault(choice => choice.ConnectionId == reader.ModelConnectionId && choice.Model == reader.Model)?.DisplayName
            : null;

    // Every bot under this one, however far down.
    public IReadOnlyList<BotSummary> Beneath => CrewTree.Beneath(OtherBots, BotId);

    public string RemoveTitle => $"Remove {Saved?.Name}";

    public static string RemoveCause => "Its conversations are kept. It stops answering and leaves the list of bots.";

    public string BeneathWords => Beneath switch
    {
        [var only] => $"{only.Name} works beneath it. Remove it too, or keep it? A kept bot moves up under {MovesUnder}.",
        _ => $"{Beneath.Count} bots work beneath it. Remove them too, or keep them? Kept bots move up under {MovesUnder}.",
    };

    public bool HasBeneath => Beneath.Count > 0;

    public string RemoveAllWords => Beneath.Count == 1 ? "Remove both" : $"Remove all {Beneath.Count + 1}";

    public static string LeaveTitle => "Leave without saving";

    public string LeaveCause => (IsNew, IsMade) switch
    {
        (true, true) => $"{ShownName} was made, but its settings are not saved yet. Leaving drops them and keeps the bot as it was made.",
        (true, false) => $"{(Draft is null ? "This bot" : ShownName)} is not made yet. Leaving drops it.",
        _ => $"The changes to {Saved?.Name} are not saved yet. Leaving drops them.",
    };

    public string SpaceRole => session.Space?.Role ?? string.Empty;

    internal Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    private string MovesUnder =>
        Saved?.ParentBotId is { } parent && OtherBots.FirstOrDefault(bot => bot.Id == parent) is { } above
            ? above.Name
            : "the top of the crew";

    #endregion

    #region Public Methods

    /// <summary>
    /// Runs when the screen is shown for a bot, with the section named in its address, such as "memory". The navigator
    /// asks this screen before it leaves while there are changes not saved.
    /// </summary>
    public async Task Open(Guid botId, string? section)
    {
        BotId = botId;
        closed = false;
        navigator.Leaving = ConfirmLeaving;
        navigator.HoldsWork = HoldsWork;

        if (shownBotId != BotId && await session.Load() is not null)
        {
            shownBotId = BotId;
            await Load();
            OpenNamedSection(section);
            Notify();
        }
    }

    /// <summary>Runs when the screen is gone, so the navigator no longer asks it.</summary>
    public void Close()
    {
        closed = true;

        if (navigator.Leaving == ConfirmLeaving)
        {
            navigator.Leaving = null;
        }

        if (navigator.HoldsWork == HoldsWork)
        {
            navigator.HoldsWork = null;
        }
    }

    /// <summary>
    /// Runs when the screen is shown for a bot that is not made yet, beneath <paramref name="parentBotId"/> when there
    /// is one. Nothing is sent to be stored: the draft starts from the connection that last answered, and saving makes
    /// the bot once.
    /// </summary>
    public async Task OpenUnmade(Guid? parentBotId)
    {
        navigator.Leaving = ConfirmLeaving;
        navigator.HoldsWork = HoldsWork;

        if (IsNew && newParentId == parentBotId && Draft is not null)
        {
            return;
        }

        if (await session.Load() is null)
        {
            return;
        }

        (IsNew, newParentId, madeBotId, madeAndOpened, createRequestId, BotId, shownBotId, leaveConfirmed) =
            (true, parentBotId, null, false, null, Guid.Empty, null, false);
        closed = false;
        await LoadNew();
    }

    /// <summary>Whether a section is on the screen. A bot that is not made yet has none that needs the bot to exist.</summary>
    public bool ShowsSection(string section) => !IsNew || section is ModelSection or MemorySection or TeamSection or LimitsSection;

    public bool IsOpen(string section) => open.Contains(section);

    public string Summary(string section) => section switch
    {
        ModelSection => BotSummaries.Model(Draft!, ConnectionName),
        ToolsSection => BotSummaries.Tools(Tools, ModelCallsTools),
        RulesSection => RuleWords.Summary(Rules),
        MemorySection => BotSummaries.Memory(Draft!),
        RoutinesSection => BotSummaries.Routines(Schedules, Saved!.Name),
        SandboxSection => BotSummaries.Sandbox(Draft!, Tools, ScreenReaderName),
        TeamSection => BotSummaries.Team(Draft!),
        _ => BotSummaries.Limits(Draft!, RunLimitOptions),
    };

    public void Toggle(string section, bool opened)
    {
        if (opened)
        {
            open.Add(section);
        }
        else if (open.Remove(section))
        {
            ForgetSection(section);
        }

        Notify();
    }

    /// <summary>Something in the draft changed: there is something to save again.</summary>
    public void Changed()
    {
        JustSaved = false;
        Notify();
    }

    /// <summary>Changes the draft as a field does, then says there is something to save.</summary>
    public void Edit(Action<BotDraft> change)
    {
        change(Draft!);
        Changed();
    }

    public void Rename(string name)
    {
        Draft!.Rename(name);
        JustSaved = false;
        Notify();
    }

    public void ChangeHandle()
    {
        EditingHandle = true;
        Notify();
    }

    [RelayCommand]
    public async Task Load()
    {
        if (IsNew)
        {
            await LoadNew();
            return;
        }

        (Saved, Draft, baseline, Models, Tools, Schedules, Rules, Readers) = (null, null, null, null, null, null, null, null);
        (LoadProblem, SaveProblem, ActionProblem, NotPermitted, Missing, JustSaved, EditingHandle) = (null, null, null, false, false, false, false);
        foreach (var section in open.ToList())
        {
            open.Remove(section);
            ForgetSection(section);
        }

        Notify();

        try
        {
            var bot = await api.GetBotDefinition(SpaceId, BotId);
            Connections = await api.ListModelConnections(SpaceId);
            OtherBots = [.. (await api.ListBots(SpaceId)).Where(other => other.Id != BotId)];
            Show(bot);
            Notify();
            Models = await api.ListModels(SpaceId, bot.ModelConnectionId);
            ToolConnections = await api.ListToolConnections(SpaceId);
            Tools = await api.ListBotTools(SpaceId, BotId);
            Schedules = await api.ListBotSchedules(SpaceId, BotId);
            WrittenPatterns = (await api.GetBotRuleOptions(SpaceId, BotId)).WrittenPatterns;
            SandboxOptions = await api.GetBotSandboxOptions(SpaceId, BotId);
            RunLimitOptions = await api.GetBotRunLimitOptions(SpaceId, BotId);
            Rules = await api.ListBotRules(SpaceId, BotId);
            SandboxStatus = await ReadSandbox();
            Readers = await ListReaders();
        }
        catch (DroverApiException refused) when (refused.StatusCode is HttpStatusCode.Forbidden)
        {
            logger.LogInformation(refused, "The {Screen} screen is not open to this person", nameof(BotEditorViewModel));
            NotPermitted = true;
        }
        catch (DroverApiException absent) when (absent.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogInformation(absent, "The {Screen} screen was asked for a bot that is not there", nameof(BotEditorViewModel));
            Missing = true;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotEditorViewModel));
            LoadProblem = Failures.Sentence(exception);
        }

        Notify();
    }

    [RelayCommand]
    public async Task Save()
    {
        if (!Dirty || Saving)
        {
            return;
        }

        if (IsNew)
        {
            await MakeBot();
            return;
        }

        if (Draft!.FindProblem() is { } unreadable)
        {
            SaveProblem = unreadable;
            Notify();
            return;
        }

        Saving = true;
        SaveProblem = null;
        Notify();
        await Try(
            async () =>
            {
                var staged = Draft.StagedGrants;
                // The server stores whether the handle was chosen, and the answer carries it back.
                Show(await api.UpdateBotDefinition(SpaceId, BotId, Draft.ToRequest()));
                await ApplyGrants(staged);
                JustSaved = true;
                EditingHandle = false;
                toasts.Show($"Settings saved for {Saved!.Name}.", ToastKind.Success);
            },
            problem => SaveProblem = problem);
        Saving = false;
        Notify();
    }

    public void Discard()
    {
        Show(Saved!);
        SaveProblem = null;
        EditingHandle = false;
        Notify();
    }

    public Task Duplicate() =>
        Try(
            async () =>
            {
                var copy = await api.DuplicateBot(SpaceId, BotId);
                toasts.Show($"{copy.Name} was made from {Saved!.Name}.");
                leaveConfirmed = true;
                navigator.NavigateTo(Addresses.Bot(copy.Id));
            },
            problem => ActionProblem = problem);

    public void MakeExtension() => navigator.NavigateTo(Addresses.ExtensionFromBot(BotId));

    public Task Pause() => SetStatus(() => api.PauseBot(SpaceId, BotId), "is paused. It starts no run until it is resumed.");

    public Task Resume() => SetStatus(() => api.ResumeBot(SpaceId, BotId), "is running again.");

    public void AskToRemove() => SetRemoving(true);

    public void SetRemoving(bool value)
    {
        Removing = value;
        Notify();
    }

    // With bots beneath, the person has said whether they go too. Without any there is nothing to ask.
    public async Task Remove(bool? removeBeneath)
    {
        Removing = false;
        Notify();
        await Try(
            async () =>
            {
                await api.RemoveBot(SpaceId, BotId, removeBeneath);
                toasts.Show($"{Saved!.Name} was removed.");
                leaveConfirmed = true;
                navigator.NavigateTo(Addresses.Bots);
            },
            problem => ActionProblem = problem);
    }

    /// <summary>
    /// Asked by the navigator before it goes to <paramref name="target"/>. With changes not saved, it stays and asks
    /// the person first.
    /// </summary>
    public bool ConfirmLeaving(string target)
    {
        if (Dirty && !leaveConfirmed)
        {
            leavingFor = target;
            Leaving = true;
            Notify();
            return false;
        }

        return true;
    }

    /// <summary>True while there are changes not saved that leaving would drop.</summary>
    public bool HoldsWork() => Dirty && !leaveConfirmed;

    public void SetLeaving(bool value)
    {
        Leaving = value;
        Notify();
    }

    public void Leave()
    {
        Leaving = false;
        leaveConfirmed = true;
        Notify();
        var target = leavingFor ?? Addresses.Bots;

        // Left by a step back, the app goes back, so the editor is not the next step back again.
        if (navigator.Previous == target)
        {
            navigator.GoBack();
        }
        else
        {
            navigator.NavigateTo(target);
        }
    }

    /// <summary>Asks the screen to read everything it shows again.</summary>
    public void Notify() => OnPropertyChanged(string.Empty);

    #endregion

    #region Internal Methods

    /// <summary>The rule is gone: offer the one press that puts it back, on this bot, wherever the person goes next.</summary>
    internal void OfferRuleUndo(BotRuleResponse rule) => OfferRuleUndo(rule, SpaceId, BotId);

    internal async Task ChooseConnection(Guid connectionId)
    {
        Models = null;
        JustSaved = false;
        Notify();
        var connection = Connections.First(candidate => candidate.Id == connectionId);

        await Try(
            async () =>
            {
                Models = await api.ListModels(SpaceId, connectionId);
                Draft!.Model = connection.DefaultModel ?? Models.FirstOrDefault(entry => entry.State is "available")?.Model ?? string.Empty;
                Draft.KeepWhatModelSupports(Models.FirstOrDefault(entry => entry.Model == Draft.Model)?.Capabilities);
            },
            problem => SaveProblem = problem);
        Models ??= [];
        Notify();
    }

    #endregion

    #region Private Methods

    private void OfferRuleUndo(BotRuleResponse rule, Guid spaceId, Guid botId) =>
        toasts.Show(
            RulesSectionViewModel.RemovedAnnouncement(rule),
            ToastKind.Info,
            new ToastAction("Undo", () => PutBack(rule, spaceId, botId), RulesSectionViewModel.UndoName(rule)));

    // Undo goes straight to the API with the ids from when the toast was made: the rule is put back on its own bot even
    // after this screen is gone. The rule gets a new id; the old one is not kept anywhere in the client.
    private async Task PutBack(BotRuleResponse rule, Guid spaceId, Guid botId)
    {
        var body = new BotRuleBody(rule.Name, rule.Enabled, rule.Checkpoint, rule.Effect, rule.Conditions, rule.Position);

        try
        {
            var saved = await api.CreateBotRule(spaceId, botId, body);
            if (!closed && BotId == botId && IsOpen(RulesSection) && Rules is not null)
            {
                RulesPart.PutBack(saved);
            }
            else
            {
                toasts.Show(RulesSectionViewModel.PutBackAnnouncement(rule), ToastKind.Success);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A rule could not be put back on the {Screen} screen", nameof(BotEditorViewModel));
            var sentence = Failures.Sentence(exception, "Check that the server is running.");
            toasts.Show(RulesSectionViewModel.PutBackFailed(sentence), ToastKind.Error);

            // Choosing the action dismisses its toast, so offer the same undo again on the bot the rule came from.
            OfferRuleUndo(rule, spaceId, botId);
        }
    }

    // A closed section is gone from the screen, and what was half done in it goes too, as it does on the web.
    private void ForgetSection(string section)
    {
        switch (section)
        {
            case ModelSection:
                ModelPart.Reset();
                break;
            case ToolsSection:
                ToolsPart.Reset();
                break;
            case RulesSection:
                RulesPart.Reset();
                break;
            case RoutinesSection:
                RoutinesPart.Reset();
                break;
        }
    }

    private void OpenNamedSection(string? section)
    {
        if (string.Equals(section, MemorySection, StringComparison.OrdinalIgnoreCase))
        {
            open.Add(MemorySection);
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
            logger.LogInformation(exception, "The sandbox state is left out of the {Screen} screen", nameof(BotEditorViewModel));
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

            foreach (var connection in Connections.OrderBy(connection => connection.Name, StringComparer.OrdinalIgnoreCase))
            {
                var listed = connection.Id == Saved!.ModelConnectionId && Models is not null ? Models : await api.ListModels(SpaceId, connection.Id);
                found.AddRange(listed
                    .Where(entry => entry.State is "available" && entry.Capabilities.Vision)
                    .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(entry => new ScreenReaderChoice(connection.Id, connection.Name, entry.Model, entry.DisplayName)));
            }

            return found;
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogInformation(exception, "The models that read images are left out of the {Screen} screen", nameof(BotEditorViewModel));
            return null;
        }
    }

    // The draft of a bot that is not made yet: the connection that last answered, its model, and a name and handle
    // the person changes. Everything is read; nothing is stored.
    private async Task LoadNew()
    {
        (Saved, Draft, baseline, Models, Tools, Schedules, Rules, Readers) = (null, null, null, null, null, null, null, null);
        (LoadProblem, SaveProblem, ActionProblem, NotPermitted, Missing, JustSaved, EditingHandle) = (null, null, null, false, false, false, false);
        Notify();

        try
        {
            if (SpaceRole is not ("owner" or "admin"))
            {
                NotPermitted = true;
                Notify();
                return;
            }

            Connections = await api.ListModelConnections(SpaceId);
            var bots = await api.ListBots(SpaceId);
            OtherBots = bots;

            if (newParentId is { } parent && bots.All(other => other.Id != parent))
            {
                Missing = true;
                Notify();
                return;
            }

            if (ConnectionChoice.Preferred(Connections) is not { } connection)
            {
                LoadProblem = NoModelCause;
                Notify();
                return;
            }

            Models = await api.ListModels(SpaceId, connection.Id);
            var model = connection.DefaultModel
                ?? bots.FirstOrDefault(bot => bot.ModelConnectionId == connection.Id)?.Model
                ?? Models.FirstOrDefault(entry => entry.State is "available")?.Model
                ?? string.Empty;
            Saved = NewBotDetail(connection.Id, model) with { ParentBotId = newParentId };
            Draft = BotDraft.From(Saved);
            baseline = BotDraft.From(Saved);
            Draft.KeepWhatModelSupports(Models.FirstOrDefault(entry => entry.Model == model)?.Capabilities);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotEditorViewModel));
            LoadProblem = Failures.Sentence(exception);
        }

        Notify();
    }

    // Makes the bot once, with the name, model and parent chosen, then sets the rest of the draft on it, in two steps.
    // The first step is never repeated: once the bot is made its id is kept, so a second Save does not make another.
    // If the second step is refused, for example because a handle that was chosen is taken by then, the draft stays on
    // screen with the reason, and the next Save sets it on the bot that was made, so nothing typed is lost.
    private async Task MakeBot()
    {
        if (madeAndOpened)
        {
            return;
        }

        if (Draft!.FindProblem() is { } unreadable)
        {
            SaveProblem = unreadable;
            Notify();
            return;
        }

        Saving = true;
        SaveProblem = null;
        Notify();
        await Try(
            async () =>
            {
                if (madeBotId is null)
                {
                    var made = await MakeOnce();
                    madeBotId = made.Id;

                    // The server made the handle from the name, numbered if taken. That is not a choice of the person's.
                    if (!Draft.HandleChosen)
                    {
                        Draft.Handle = made.Handle;
                        Draft.HandleChosen = false;
                    }
                }

                var id = madeBotId.Value;
                await api.UpdateBotDefinition(SpaceId, id, Draft.ToRequest());
                leaveConfirmed = true;
                madeAndOpened = true;
                toasts.Show($"{Draft.Name} was made.", ToastKind.Success);
                navigator.NavigateTo(Addresses.Bot(id));
            },
            problem => SaveProblem = madeBotId is null ? problem : $"{MadeButNotSet} {problem}");
        Saving = false;
        Notify();
    }

    // Sends the create. It carries the request id chosen for this draft, so a create whose answer was lost and is sent
    // again returns the bot that was made. A bot is never found by what it is called or says: another person's bot of the
    // same name is not this one.
    private async Task<BotSummary> MakeOnce()
    {
        createRequestId ??= Guid.CreateVersion7();

        try
        {
            return await api.CreateBot(SpaceId, new(Draft!.Name, Draft.Instructions, Draft.ModelConnectionId, Draft.Model, newParentId, createRequestId));
        }
        catch (DroverApiException refused) when (refused.StatusCode < HttpStatusCode.InternalServerError)
        {
            // The server answered that it did not make the bot, so the request id is not spoken for: the next try chooses another.
            logger.LogInformation(refused, "The server refused to make a bot on the {Screen} screen", nameof(BotEditorViewModel));
            createRequestId = null;
            throw;
        }
    }

    private static BotDetail NewBotDetail(Guid connectionId, string model) => new(
        Guid.Empty,
        NewBotName,
        BotHandles.Stem(NewBotName),
        new BotAvatarBody(null, null),
        string.Empty,
        string.Empty,
        connectionId,
        model,
        new ModelSettingsBody(null, null, null, "off", false),
        new MemorySettingsBody(false, "none", null, null),
        "none",
        new SandboxSettingsBody("off", [], null),
        [],
        "active",
        null,
        0,
        new BuildLimitsBody(null, null),
        new RunLimitsBody(null, null, null),
        null,
        null);

    private void Show(BotDetail bot)
    {
        Saved = bot;
        Draft = BotDraft.From(bot);
        baseline = BotDraft.From(bot);
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
            Tools = [.. (Tools ?? []).Select(tool => tool.ToolId == changed.ToolId ? changed : tool)];
        }
    }

    // Only the status is taken from the answer, so what is being typed is kept.
    private Task SetStatus(Func<Task<BotDetail>> change, string outcome) =>
        Try(
            async () =>
            {
                Saved = Saved! with { Status = (await change()).Status };
                toasts.Show($"{Saved.Name} {outcome}");
            },
            problem => ActionProblem = problem);

    private async Task Try(Func<Task> work, Action<string> report)
    {
        ActionProblem = null;

        try
        {
            await work();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(BotEditorViewModel));
            report(Failures.Sentence(exception, "Check that the server is running."));
        }

        Notify();
    }

    #endregion
}
