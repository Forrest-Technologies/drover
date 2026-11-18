namespace Drover.Presentation.ViewModels;

/// <summary>
/// The tool connections of the space. Its job: add a tool server, see whether it is connected, sign in to it,
/// review changed tools, remove it.
/// </summary>
public sealed partial class ToolConnectionsViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigator,
    IServiceProvider services,
    ILogger<ToolConnectionsViewModel> logger) : ObservableObject
{
    #region Private Fields

    private static readonly string[] stateNames =
    [
        nameof(ShowAddButton), nameof(ShowFailed), nameof(ShowLoading), nameof(ShowEmptyToAdd), nameof(ShowEmptyReadOnly),
        nameof(ShowList), nameof(CanEdit), nameof(Adding), nameof(RemoveOpen), nameof(RemoveTitle),
    ];

    private readonly Dictionary<Guid, IReadOnlyList<ConnectionToolSummary>> tools = [];

    private readonly Dictionary<Guid, string> problems = [];

    private List<ToolConnectionSummary>? connections;

    private ToolConnectionSummary? removing;

    private Guid? busy;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    private string? notice;

    [ObservableProperty]
    private NoteKind noticeKind = NoteKind.Ok;

    [ObservableProperty]
    private AddConnectionFormViewModel? form;

    #endregion

    #region Properties

    public static string Title => "Tool connections";

    public static string PageTitle => "Tool connections · Drover";

    public static string AddLabel => "Add a tool server";

    public static string LoadingNote => "Loading the tool connections.";

    public static string LoadFailedTitle => "Drover could not load the tool connections";

    public static string EmptyTitle => "No tool server yet";

    public static string EmptyCause =>
        "A tool server gives bots tools beyond the built-in ones. Add one by its address, then give its tools to a bot.";

    public static string EmptyReadOnlyNote => "This space has no tool servers yet. An owner or admin of the space can add one.";

    public static string RemoveBody =>
        "Bots lose its tools at once, and its stored credential and sign-ins are deleted. Runs that already used it keep their history.";

    /// <summary>The cards of the list, one for each connection.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ToolConnectionCardModel> Cards { get; } = [];

    public bool Adding => Form is not null;

    public bool ShowAddButton => CanEdit && connections is { Count: > 0 } && !Adding;

    public bool ShowFailed => LoadProblem is not null;

    public bool ShowLoading => LoadProblem is null && connections is null;

    public bool ShowEmptyToAdd => LoadProblem is null && connections is { Count: 0 } && !Adding && CanEdit;

    public bool ShowEmptyReadOnly => LoadProblem is null && connections is { Count: 0 } && !Adding && !CanEdit;

    public bool ShowList => LoadProblem is null && connections is { Count: > 0 };

    public string RemoveTitle => $"Remove {removing?.Name}";

    /// <summary>The dialog that asks before a server is removed is open while one is waiting to be.</summary>
    public bool RemoveOpen
    {
        get => removing is not null;
        set
        {
            if (!value && removing is not null)
            {
                removing = null;
                Show();
            }
        }
    }

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");


    /// <summary>
    /// The server wants the person at the provider's own page to sign in. It is a page outside the app, which the
    /// platform opens; the provider sends the browser back when done.
    /// </summary>
    public event Action<string>? ProviderPageRequested;

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows the screen. <paramref name="signInOutcome"/> and <paramref name="signInConnection"/> are how a sign-in at
    /// the provider ended, when the browser has just come back from one; <paramref name="ticket"/> is the one-time
    /// ticket a browser carried into the native app after it, which this screen ends the sign-in with, once, and
    /// takes off the address.
    /// </summary>
    public async Task Open(string? signInOutcome, Guid? signInConnection, string? ticket)
    {
        if (await session.Load() is not null)
        {
            var finished = await FinishSignIn(ticket);
            await Load();
            SayHowSignInEnded(finished?.Outcome ?? signInOutcome, finished?.ConnectionId ?? signInConnection);
        }
    }

    [RelayCommand]
    public async Task Load()
    {
        LoadProblem = null;

        try
        {
            connections = await api.ListToolConnections(SpaceId);

            foreach (var connection in connections)
            {
                tools[connection.Id] = await api.ListConnectionTools(SpaceId, connection.Id);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnectionsViewModel));
            LoadProblem = Failures.Sentence(exception);
        }

        Show();
    }

    [RelayCommand]
    public void StartAdding()
    {
        Form = new(Add, StopAdding);
        Notice = null;
        Show();
    }

    public Task Refresh(ToolConnectionSummary connection) =>
        Change(connection, async () =>
        {
            var refreshed = await api.RefreshToolConnection(SpaceId, connection.Id);
            Replace(refreshed.Connection);
            tools[connection.Id] = refreshed.Tools;
            Say(refreshed.Connection, refreshed.Problem, Listed(refreshed));
        });

    public Task SignIn(ToolConnectionSummary connection) =>
        Change(connection, async () =>
        {
            var start = await api.BeginToolSignIn(SpaceId, connection.Id, services.GetService<Drover.Ui.Pages.Tools.AppSignInReturn>()?.Scheme);

            if (start is { Step: "redirect", AuthorizationUrl: { } address })
            {
                // The provider's own page: away from the app. It sends the browser back when done.
                ProviderPageRequested?.Invoke(address);
                return;
            }

            await Reload(connection);
            Say(connection, start.Step is "connected" ? null : start.Problem ?? "The sign-in could not be started.", $"You are signed in to {connection.Name}.");
        });

    public Task SignOut(ToolConnectionSummary connection) =>
        Change(connection, async () =>
        {
            await api.RemoveToolSignIn(SpaceId, connection.Id);
            await Reload(connection);
            Say(connection, null, $"The sign-in to {connection.Name} is removed.");
        });

    public Task ReplaceHeader(ToolConnectionSummary connection, ReplaceToolHeaderRequest request) =>
        Change(connection, async () =>
        {
            Replace(await api.ReplaceToolHeader(SpaceId, connection.Id, request));
            Say(connection, null, $"The header for {connection.Name} is replaced.");
        });

    public Task Accept(ConnectionToolSummary tool)
    {
        var connection = connections!.First(candidate => candidate.Id == tool.ConnectionId);
        return Change(connection, async () =>
        {
            try
            {
                var accepted = await api.ConfirmToolReview(SpaceId, connection.Id, tool.Id, tool.DefinitionHash);
                tools[connection.Id] = [.. tools[connection.Id].Select(listed => listed.Id == accepted.Id ? accepted : listed)];
                Say(connection, null, $"The change to {accepted.Title ?? accepted.Name} is accepted.");
            }
            catch (DroverApiException changedAgain) when (changedAgain.StatusCode is HttpStatusCode.Conflict)
            {
                // The tool changed while it was being read: show what it says now, so the next yes is to that.
                tools[connection.Id] = await api.ListConnectionTools(SpaceId, connection.Id);
                throw;
            }
        });
    }

    public void AskToRemove(ToolConnectionSummary connection)
    {
        removing = connection;
        Show();
    }

    [RelayCommand]
    public void KeepServer()
    {
        removing = null;
        Show();
    }

    [RelayCommand]
    public async Task Remove()
    {
        var connection = removing!;
        removing = null;
        await Change(connection, async () =>
        {
            await api.RemoveToolConnection(SpaceId, connection.Id);
            connections!.Remove(connection);
            tools.Remove(connection.Id);
            Say(connection, null, $"{connection.Name} is removed.");
        });
    }

    #endregion

    #region Private Methods

    // The sign-in was started here, in a native app, and its browser has just come back into the app. Only the
    // server can end it, for the person who started it; a ticket that is not theirs ends as a failure.
    private async Task<(string Outcome, Guid? ConnectionId)?> FinishSignIn(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket))
        {
            return null;
        }

        (string Outcome, Guid? ConnectionId) finished = ("failed", null);

        try
        {
            var finish = await api.FinishToolSignIn(ticket);
            finished = (finish.Connected ? "connected" : "failed", finish.ConnectionId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnectionsViewModel));
        }

        navigator.NavigateTo(Addresses.ToolSignIn(finished.Outcome, finished.ConnectionId?.ToString()), replace: true);
        return finished;
    }

    // The address carries only one of two words this server chose and an id; the name is looked up here.
    private void SayHowSignInEnded(string? outcome, Guid? connectionId)
    {
        if (outcome is not ("connected" or "failed"))
        {
            return;
        }

        var name = connections?.Find(connection => connection.Id == connectionId)?.Name ?? "the tool server";
        var failed = outcome is "failed";
        NoticeKind = failed ? NoteKind.Danger : NoteKind.Ok;
        Notice = failed
            ? $"The sign-in to {name} did not finish. Nothing was stored. Try again."
            : $"You are signed in to {name}.";
        Show();
    }

    private void StopAdding()
    {
        Form = null;
        Show();
    }

    private async Task Add(ConnectionDraft draft)
    {
        var adding = Form!;
        adding.Busy = true;
        adding.Problem = null;

        try
        {
            var added = await api.CreateToolConnection(SpaceId, draft.ToRequest());
            connections!.Add(added.Connection);
            tools[added.Connection.Id] = added.Tools;
            Form = null;
            Say(added.Connection, added.Problem, $"{added.Connection.Name} is added.");
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnectionsViewModel));
            adding.Problem = Failures.Sentence(exception);
        }
        finally
        {
            adding.Busy = false;
            Show();
        }
    }

    private async Task Reload(ToolConnectionSummary connection)
    {
        if ((await api.ListToolConnections(SpaceId)).Find(listed => listed.Id == connection.Id) is { } current)
        {
            Replace(current);
        }
    }

    private void Replace(ToolConnectionSummary current) =>
        connections![connections.FindIndex(listed => listed.Id == current.Id)] = current;

    private static string Listed(ToolConnectionRefresh refreshed)
    {
        List<string> parts = [refreshed.Tools.Count == 1 ? "1 tool listed" : $"{refreshed.Tools.Count} tools listed"];

        if (refreshed.Added > 0)
        {
            parts.Add($"{refreshed.Added} new");
        }

        if (refreshed.Changed > 0)
        {
            parts.Add($"{refreshed.Changed} changed");
        }

        if (refreshed.Missing > 0)
        {
            parts.Add($"{refreshed.Missing} no longer listed");
        }

        return $"{refreshed.Connection.Name}: {string.Join(", ", parts)}.";
    }

    // A problem belongs to its connection and is shown on its card; what went well is said once at the top.
    private void Say(ToolConnectionSummary connection, string? problem, string done)
    {
        if (problem is null)
        {
            problems.Remove(connection.Id);
            (Notice, NoticeKind) = (done, NoteKind.Ok);
        }
        else
        {
            problems[connection.Id] = problem;
            Notice = null;
        }
    }

    private async Task Change(ToolConnectionSummary connection, Func<Task> work)
    {
        busy = connection.Id;
        problems.Remove(connection.Id);
        Notice = null;
        Show();

        try
        {
            await work();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnectionsViewModel));
            problems[connection.Id] = Failures.Sentence(exception);
        }
        finally
        {
            busy = null;
            Show();
        }
    }

    // Gives each card what the screen's state says about its connection, and tells the page what to show.
    private void Show()
    {
        SyncCards();
        foreach (var stateName in stateNames)
        {
            OnPropertyChanged(stateName);
        }
    }

    private void SyncCards()
    {
        var wanted = connections ?? [];

        for (var index = 0; index < wanted.Count; index++)
        {
            var connection = wanted[index];
            var card = Cards.FirstOrDefault(existing => existing.Id == connection.Id) ?? new ToolConnectionCardModel(this, connection);
            card.Update(connection, tools.GetValueOrDefault(connection.Id), CanEdit, busy == connection.Id, problems.GetValueOrDefault(connection.Id));

            if (index >= Cards.Count)
            {
                Cards.Add(card);
            }
            else if (Cards[index].Id != connection.Id)
            {
                Cards.Remove(card);
                Cards.Insert(index, card);
            }
        }

        while (Cards.Count > wanted.Count)
        {
            Cards.RemoveAt(Cards.Count - 1);
        }
    }

    #endregion
}
