namespace Drover.Presentation.ViewModels;

/// <summary>
/// The tool connections of the space. Its job: add a tool server, see whether it is connected, sign in to it,
/// review changed tools, remove it.
/// </summary>
public sealed partial class ToolConnectionsViewModel(
    DroverApi api,
    SessionState session,
    Navigator navigation,
    ILogger<ToolConnectionsViewModel> logger) : ObservableObject
{
    #region Private Fields

    private readonly Dictionary<Guid, IReadOnlyList<ConnectionToolSummary>> tools = [];

    private readonly Dictionary<Guid, string> problems = [];

    [ObservableProperty]
    private List<ToolConnectionSummary>? connections;

    [ObservableProperty]
    private ToolConnectionSummary? removing;

    [ObservableProperty]
    private Guid? busy;

    [ObservableProperty]
    private string? loadProblem;

    [ObservableProperty]
    private string? addProblem;

    [ObservableProperty]
    private string? notice;

    [ObservableProperty]
    private bool noticeIsProblem;

    [ObservableProperty]
    private bool adding;

    [ObservableProperty]
    private bool addingBusy;

    #endregion

    #region Properties

    /// <summary>How a sign-in at the provider ended, when the browser has just come back from one.</summary>
    public string? SignInOutcome { get; set; }

    /// <summary>The connection that sign-in was for.</summary>
    public Guid? SignInConnection { get; set; }

    /// <summary>
    /// The one-time ticket a browser carried into the native app after a sign-in at the provider. This screen
    /// ends that sign-in with it, once, and takes it off the address.
    /// </summary>
    public string? SignInTicket { get; set; }

    public IReadOnlyDictionary<Guid, IReadOnlyList<ConnectionToolSummary>> Tools => tools;

    public IReadOnlyDictionary<Guid, string> Problems => problems;

    public bool ChangePending => Busy is not null || AddingBusy;

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    public bool CanEdit => session.Space?.Role is "owner" or "admin";

    #endregion

    #region Public Methods

    public async Task Open(string? outcome = null, Guid? connection = null, string? ticket = null)
    {
        (SignInOutcome, SignInConnection, SignInTicket) = (outcome, connection, ticket);
        if (await session.Load() is not null)
        {
            var finished = await FinishSignIn();
            await Load();
            SayHowSignInEnded(finished?.Outcome ?? SignInOutcome, finished?.ConnectionId ?? SignInConnection);
        }
    }

    #endregion

    #region Private Methods

    public async Task Load()
    {
        LoadProblem = null;

        try
        {
            Connections = await api.ListToolConnections(SpaceId);

            foreach (var connection in Connections)
            {
                tools[connection.Id] = await api.ListConnectionTools(SpaceId, connection.Id);
            }
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnectionsViewModel));
            LoadProblem = Failures.Sentence(exception);
        }
    }

    // The sign-in was started here, in a native app, and its browser has just come back into the app. Only the
    // server can end it, for the person who started it; a ticket that is not theirs ends as a failure.
    private async Task<(string Outcome, Guid? ConnectionId)?> FinishSignIn()
    {
        if (string.IsNullOrEmpty(SignInTicket))
        {
            return null;
        }

        (string Outcome, Guid? ConnectionId) finished = ("failed", null);

        try
        {
            var finish = await api.FinishToolSignIn(SignInTicket);
            finished = (finish.Connected ? "connected" : "failed", finish.ConnectionId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnectionsViewModel));
        }

        navigation.NavigateTo(Addresses.ToolSignIn(finished.Outcome, finished.ConnectionId?.ToString()), replace: true);
        return finished;
    }

    // The address carries only one of two words this server chose and an id; the name is looked up here.
    private void SayHowSignInEnded(string? outcome, Guid? connectionId)
    {
        if (outcome is not ("connected" or "failed"))
        {
            return;
        }

        var name = Connections?.Find(connection => connection.Id == connectionId)?.Name ?? "the tool server";
        NoticeIsProblem = outcome is "failed";
        Notice = NoticeIsProblem
            ? $"The sign-in to {name} did not finish. Nothing was stored. Try again."
            : $"You are signed in to {name}.";
    }

    public void StartAdding()
    {
        Adding = true;
        AddProblem = null;
        Notice = null;
    }

    public async Task Add(ConnectionDraft draft)
    {
        AddingBusy = true;
        AddProblem = null;

        try
        {
            var added = await api.CreateToolConnection(SpaceId, draft.ToRequest());
            Connections!.Add(added.Connection);
            tools[added.Connection.Id] = added.Tools;
            Adding = false;
            Say(added.Connection, added.Problem, $"{added.Connection.Name} is added.");
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnectionsViewModel));
            AddProblem = Failures.Sentence(exception);
        }
        finally
        {
            AddingBusy = false;
        }
    }

    public Task Refresh(ToolConnectionSummary connection) =>
        Change(connection, async () =>
        {
            var refreshed = await api.RefreshToolConnection(SpaceId, connection.Id);
            Replace(refreshed.Connection);
            tools[connection.Id] = refreshed.Tools;
            Say(refreshed.Connection, refreshed.Problem, Listed(refreshed));
        });

    public Task SignIn(ToolConnectionSummary connection, Func<string, Task> openAddress, string? returnTo = null) =>
        Change(connection, async () =>
        {
            var start = await api.BeginToolSignIn(SpaceId, connection.Id, returnTo);

            if (start is { Step: "redirect", AuthorizationUrl: { } address })
            {
                // The provider's own page: a full load, away from the app. It sends the browser back when done.
                await openAddress(address);
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
        var connection = Connections!.First(candidate => candidate.Id == tool.ConnectionId);
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

    public async Task Remove()
    {
        var connection = Removing!;
        Removing = null;
        await Change(connection, async () =>
        {
            await api.RemoveToolConnection(SpaceId, connection.Id);
            Connections!.Remove(connection);
            tools.Remove(connection.Id);
            Say(connection, null, $"{connection.Name} is removed.");
        });
    }

    private async Task Reload(ToolConnectionSummary connection)
    {
        if ((await api.ListToolConnections(SpaceId)).Find(listed => listed.Id == connection.Id) is { } current)
        {
            Replace(current);
        }
    }

    private void Replace(ToolConnectionSummary current) =>
        Connections![Connections.FindIndex(listed => listed.Id == current.Id)] = current;

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
            (Notice, NoticeIsProblem) = (done, false);
        }
        else
        {
            problems[connection.Id] = problem;
            Notice = null;
        }
    }

    private async Task Change(ToolConnectionSummary connection, Func<Task> work)
    {
        Busy = connection.Id;
        problems.Remove(connection.Id);
        Notice = null;

        try
        {
            await work();
        }
        catch (DeviceCallFailedException exception)
        {
            logger.LogWarning(exception, "The device could not open the tool sign-in page");
            problems[connection.Id] = "The sign-in page could not be opened. Try again.";
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnectionsViewModel));
            problems[connection.Id] = Failures.Sentence(exception);
        }
        finally
        {
            Busy = null;
        }
    }

    #endregion
}
