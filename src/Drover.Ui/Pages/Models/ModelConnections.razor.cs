namespace Drover.Ui.Pages.Models;

/// <summary>
/// The model connections of the space. Its job: see whether each connection works, add one, test it, replace its key.
/// </summary>
public sealed partial class ModelConnections(
    DroverApi api,
    SessionState session,
    ILogger<ModelConnections> logger)
{
    #region Private Fields

    private const string VeniceKind = "venice";

    private const string CompatibleKind = "openai_compatible";

    private static readonly SegmentOption<string>[] providers =
    [
        new(VeniceKind, "Venice"),
        new(CompatibleKind, "Compatible endpoint"),
    ];

    private readonly Dictionary<Guid, string> problems = [];

    private List<ModelConnectionSummary>? connections;

    private string kind = VeniceKind;

    private string baseUrl = string.Empty;

    private string apiKey = string.Empty;

    private string newKey = string.Empty;

    private Guid? replacing;

    private Guid? busy;

    private string? loadProblem;

    private string? addProblem;

    private string? notice;

    private bool noticeIsProblem;

    private bool adding;

    private bool addingBusy;

    #endregion

    #region Properties

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
        }
    }

    #endregion

    #region Private Methods

    private static (BadgeKind Kind, string Words) StatusOf(ModelConnectionSummary connection) => connection.Status switch
    {
        "ready" => (BadgeKind.Ok, "Working"),
        "failed" => (BadgeKind.Danger, "Not working"),
        _ => (BadgeKind.Neutral, "Not tested"),
    };

    private static string Facts(ModelConnectionSummary connection)
    {
        var kindWords = connection.Kind == CompatibleKind ? "compatible endpoint" : connection.Kind;
        var key = connection.HasKey ? "key stored" : "no key";
        var checkedWords = connection.LastCheckedAt is { } at ? $"checked {at.ToLocalTime():g}" : "never checked";
        return $"{kindWords} · {key} · {checkedWords}";
    }

    private async Task Load()
    {
        loadProblem = null;

        try
        {
            connections = [.. await api.ListModelConnections(SpaceId)];
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnections));
            loadProblem = Failures.Sentence(exception);
        }
    }

    private void ChooseProvider(string chosen)
    {
        kind = chosen;
        addProblem = null;
    }

    private void StartAdding()
    {
        adding = true;
        addProblem = null;
        notice = null;
        apiKey = string.Empty;
        baseUrl = string.Empty;
    }

    private void StartReplacing(ModelConnectionSummary connection)
    {
        replacing = connection.Id;
        newKey = string.Empty;
    }

    private async Task Add()
    {
        addingBusy = true;
        addProblem = null;

        try
        {
            var address = kind == CompatibleKind ? baseUrl.Trim() : null;
            var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            var added = await api.CreateModelConnection(SpaceId, new(kind, NameFor(address), address, key, null));
            connections!.Add(added);
            adding = false;
            apiKey = string.Empty;
            await Test(added);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnections));
            addProblem = Failures.Sentence(exception);
        }
        finally
        {
            addingBusy = false;
        }
    }

    private Task Test(ModelConnectionSummary connection) =>
        Change(connection, async () =>
        {
            var result = await api.TestModelConnection(SpaceId, connection.Id);
            Replace(result.Connection);
            Say(result.Succeeded ? null : result.Message, result.Message);
        });

    private Task ReplaceKey(ModelConnectionSummary connection) =>
        Change(connection, async () =>
        {
            Replace(await api.ReplaceModelKey(SpaceId, connection.Id, new(newKey.Trim())));
            newKey = string.Empty;
            replacing = null;
            Say(null, $"The key for {connection.Name} is replaced. Test the connection to check it.");
        });

    private async Task Change(ModelConnectionSummary connection, Func<Task> action)
    {
        busy = connection.Id;
        problems.Remove(connection.Id);

        try
        {
            await action();
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "A request to the server failed on the {Screen} screen", nameof(ModelConnections));
            problems[connection.Id] = Failures.Sentence(exception);
            notice = null;
        }
        finally
        {
            busy = null;
        }
    }

    private void Replace(ModelConnectionSummary changed)
    {
        var index = connections!.FindIndex(connection => connection.Id == changed.Id);
        if (index >= 0)
        {
            connections[index] = changed;
        }
    }

    private void Say(string? problem, string words)
    {
        noticeIsProblem = problem is not null;
        notice = words;
    }

    private static string NameFor(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var parsed) ? parsed.Host : "Venice";

    #endregion
}
