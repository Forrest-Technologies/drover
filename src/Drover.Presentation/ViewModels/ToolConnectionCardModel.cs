namespace Drover.Presentation.ViewModels;

/// <summary>
/// One tool server of the screen: whether it answers, what it needs from the reader, its tools, and what can be done
/// with it. The screen's view model keeps the state and tells each card what it is.
/// </summary>
public sealed partial class ToolConnectionCardModel(ToolConnectionsViewModel owner, ToolConnectionSummary connection) : ObservableObject
{
    #region Private Fields

    private static readonly string[] everything =
    [
        nameof(Connection), nameof(Name), nameof(Url), nameof(Facts), nameof(StatusKind), nameof(StatusWords), nameof(ShownError),
        nameof(ShownProblem), nameof(SignInNeed), nameof(ShowSignInAsk), nameof(ShowSignInButton), nameof(ShowSignInHint),
        nameof(SignInLabel), nameof(ToolRows), nameof(HasTools), nameof(ToolsOpen), nameof(ShowNoTools), nameof(CheckLabel),
        nameof(ShowReplaceHeaderButton), nameof(ShowSignOut), nameof(SignOutLabel), nameof(RemoveLabel), nameof(Busy),
        nameof(NotBusy), nameof(CanEdit),
    ];

    private ToolConnectionSummary current = connection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReplaceHeaderButton))]
    private bool replacingHeader;

    [ObservableProperty]
    private string headerName = "Authorization";

    [ObservableProperty]
    private string headerValue = string.Empty;

    #endregion

    #region Properties

    public static string SignInHint => "An owner or admin of the space signs in for everyone.";

    public static string NoToolsNote => "The server answers and lists no tools.";

    public static string ToolsHeading => "Tools";

    public static string ReplaceHeaderHeading => "Replace the header";

    public static string HeaderHint => "Stored encrypted. It is not shown again.";

    public Guid Id => current.Id;

    public ToolConnectionSummary Connection => current;

    public IReadOnlyList<ConnectionToolSummary>? Tools { get; private set; }

    /// <summary>The reader is an owner or admin of the space. The server decides this again on every request.</summary>
    public bool CanEdit { get; private set; }

    public bool Busy { get; private set; }

    public bool NotBusy => !Busy;

    /// <summary>What went wrong with the last thing done to this connection.</summary>
    public string? Problem { get; private set; }

    public string Name => current.Name;

    public string Url => current.Url;

    public string Facts => ConnectionWords.Facts(current, Tools);

    public BadgeKind StatusKind => ConnectionWords.Status(current).Kind;

    public string StatusWords => ConnectionWords.Status(current).Words;

    public string? ShownError => current.Status is "failed" && current.LastError is { Length: > 0 } error ? error : null;

    // A check that just failed leaves the same words as the connection's last error; they are said once.
    public string? ShownProblem => Problem == ShownError ? null : Problem;

    public string? SignInNeed => ConnectionWords.SignInNeed(current);

    // One sign-in for the whole space is an owner's or admin's to give; a person's own sign-in is theirs.
    public bool MaySignIn => current.SignInScope is "per_user" || CanEdit;

    public bool ShowSignInAsk => SignInNeed is not null;

    public bool ShowSignInButton => SignInNeed is not null && MaySignIn;

    public bool ShowSignInHint => SignInNeed is not null && !MaySignIn;

    public string SignInLabel => current.SignIn is "needs_reconnect" ? "Sign in again" : "Sign in";

    public IReadOnlyList<ConnectionToolRow> ToolRows { get; private set; } = [];

    public bool HasTools => Tools is { Count: > 0 };

    public bool ToolsOpen => Tools?.Any(tool => tool.NeedsReview) ?? false;

    public bool ShowNoTools => Tools is { Count: 0 } && current.Status is "ready";

    public string CheckLabel => Busy ? "Working" : "Check and list tools";

    public bool ShowReplaceHeaderButton => CanEdit && current.AuthKind is "header" && !ReplacingHeader;

    public bool ShowSignOut => current.SignIn is "signed_in" && MaySignIn;

    public string SignOutLabel => $"Sign out of {current.Name}";

    public string RemoveLabel => $"Remove {current.Name}";

    #endregion

    #region Public Methods

    /// <summary>Takes what the screen's state says about this connection, and says what changed.</summary>
    public void Update(ToolConnectionSummary summary, IReadOnlyList<ConnectionToolSummary>? tools, bool canEdit, bool busy, string? problem)
    {
        current = summary;
        (Tools, CanEdit, Busy, Problem) = (tools, canEdit, busy, problem);
        ToolRows = [.. (tools ?? []).Select(tool => new ConnectionToolRow(tool, this, canEdit))];
        foreach (var name in everything)
        {
            OnPropertyChanged(name);
        }
    }

    [RelayCommand]
    public Task SignIn() => owner.SignIn(current);

    [RelayCommand]
    public Task SignOut() => owner.SignOut(current);

    [RelayCommand]
    public Task Refresh() => owner.Refresh(current);

    [RelayCommand]
    public void Remove() => owner.AskToRemove(current);

    [RelayCommand]
    public void StartReplacingHeader() => ReplacingHeader = true;

    [RelayCommand]
    public void StopReplacing()
    {
        ReplacingHeader = false;
        HeaderValue = string.Empty;
    }

    [RelayCommand]
    public async Task SaveHeader()
    {
        await owner.ReplaceHeader(current, new ReplaceToolHeaderRequest(HeaderName.Trim(), HeaderValue));
        StopReplacing();
    }

    public Task Accept(ConnectionToolSummary tool) => owner.Accept(tool);

    #endregion
}
