namespace Drover.Ui.Pages.Tools;

/// <summary>
/// The tool connections of the space. Its job: add a tool server, see whether it is connected, sign in to it,
/// review changed tools, remove it.
/// </summary>
public sealed partial class ToolConnections(
    DroverApi api,
    SessionState session,
    NavigationManager navigation,
    ILogger<ToolConnections> logger)
{
    #region Private Fields

    private readonly Dictionary<Guid, IReadOnlyList<ConnectionToolSummary>> tools = [];

    private readonly Dictionary<Guid, string> problems = [];

    private List<ToolConnectionSummary>? connections;

    private ToolConnectionSummary? removing;

    private Guid? busy;

    private string? loadProblem;

    private string? addProblem;

    private string? notice;

    private bool noticeIsProblem;

    private bool adding;

    private bool addingBusy;

    #endregion

    #region Properties

    /// <summary>How a sign-in at the provider ended, when the browser has just come back from one.</summary>
    [SupplyParameterFromQuery(Name = "sign-in")]
    public string? SignInOutcome { get; set; }

    /// <summary>The connection that sign-in was for.</summary>
    [SupplyParameterFromQuery(Name = "connection")]
    public Guid? SignInConnection { get; set; }

    private Guid SpaceId => session.Space?.Id ?? throw new InvalidOperationException("A signed-in person always has a space.");

    // The server decides this for every request. Here it only keeps controls a person cannot use off the screen.
    private bool CanEdit => session.Space?.Role is "owner" or "admin";

    #endregion

    #region Overrides

    protected override async Task OnInitializedAsync()
    {
        if (await session.Load() is not null)
        {
            await Load();
            SayHowSignInEnded();
        }
    }

    #endregion

    #region Private Methods

    private async Task Load()
    {
        loadProblem = null;

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
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnections));
            loadProblem = Failures.Sentence(exception);
        }
    }

    // The address carries only one of two words this server chose and an id; the name is looked up here.
    private void SayHowSignInEnded()
    {
        if (SignInOutcome is not ("connected" or "failed"))
        {
            return;
        }

        var name = connections?.Find(connection => connection.Id == SignInConnection)?.Name ?? "the tool server";
        noticeIsProblem = SignInOutcome is "failed";
        notice = noticeIsProblem
            ? $"The sign-in to {name} did not finish. Nothing was stored. Try again."
            : $"You are signed in to {name}.";
    }

    private void StartAdding()
    {
        adding = true;
        addProblem = null;
        notice = null;
    }

    private async Task Add(ConnectionDraft draft)
    {
        addingBusy = true;
        addProblem = null;

        try
        {
            var added = await api.CreateToolConnection(SpaceId, draft.ToRequest());
            connections!.Add(added.Connection);
            tools[added.Connection.Id] = added.Tools;
            adding = false;
            Say(added.Connection, added.Problem, $"{added.Connection.Name} is added.");
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnections));
            addProblem = Failures.Sentence(exception);
        }
        finally
        {
            addingBusy = false;
        }
    }

    private Task Refresh(ToolConnectionSummary connection) =>
        Change(connection, async () =>
        {
            var refreshed = await api.RefreshToolConnection(SpaceId, connection.Id);
            Replace(refreshed.Connection);
            tools[connection.Id] = refreshed.Tools;
            Say(refreshed.Connection, refreshed.Problem, Listed(refreshed));
        });

    private Task SignIn(ToolConnectionSummary connection) =>
        Change(connection, async () =>
        {
            var start = await api.BeginToolSignIn(SpaceId, connection.Id);

            if (start is { Step: "redirect", AuthorizationUrl: { } address })
            {
                // The provider's own page: a full load, away from the app. It sends the browser back when done.
                navigation.NavigateTo(address, forceLoad: true);
                return;
            }

            await Reload(connection);
            Say(connection, start.Step is "connected" ? null : start.Problem ?? "The sign-in could not be started.", $"You are signed in to {connection.Name}.");
        });

    private Task SignOut(ToolConnectionSummary connection) =>
        Change(connection, async () =>
        {
            await api.RemoveToolSignIn(SpaceId, connection.Id);
            await Reload(connection);
            Say(connection, null, $"The sign-in to {connection.Name} is removed.");
        });

    private Task ReplaceHeader(ToolConnectionSummary connection, ReplaceToolHeaderRequest request) =>
        Change(connection, async () =>
        {
            Replace(await api.ReplaceToolHeader(SpaceId, connection.Id, request));
            Say(connection, null, $"The header for {connection.Name} is replaced.");
        });

    private Task Accept(ConnectionToolSummary tool)
    {
        var connection = connections!.First(candidate => candidate.Id == tool.ConnectionId);
        return Change(connection, async () =>
        {
            var accepted = await api.ConfirmToolReview(SpaceId, connection.Id, tool.Id);
            tools[connection.Id] = [.. tools[connection.Id].Select(listed => listed.Id == accepted.Id ? accepted : listed)];
            Say(connection, null, $"The change to {accepted.Title ?? accepted.Name} is accepted.");
        });
    }

    private async Task Remove()
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
            (notice, noticeIsProblem) = (done, false);
        }
        else
        {
            problems[connection.Id] = problem;
            notice = null;
        }
    }

    private async Task Change(ToolConnectionSummary connection, Func<Task> work)
    {
        busy = connection.Id;
        problems.Remove(connection.Id);
        notice = null;

        try
        {
            await work();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ToolConnections));
            problems[connection.Id] = Failures.Sentence(exception);
        }
        finally
        {
            busy = null;
        }
    }

    #endregion
}
