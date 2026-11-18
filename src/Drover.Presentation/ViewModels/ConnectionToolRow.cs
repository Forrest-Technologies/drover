namespace Drover.Presentation.ViewModels;

/// <summary>
/// One tool of a tool server: what it says it is, whether the server still lists it, and, when the server changed
/// what it says, what was reviewed beside what it says now.
/// </summary>
public sealed partial class ConnectionToolRow(ConnectionToolSummary tool, ToolConnectionCardModel card, bool canEdit) : ObservableObject
{
    #region Properties

    public static string MissingNote => "The server no longer lists this tool, so no bot can use it.";

    public static string ReviewBadge => "Changed, needs review";

    public static string ReviewNote =>
        "The server changed what this tool says it does. Bots that have it ask before each use until the change is accepted.";

    public static string SameWordsNote => "The words are the same. What the tool accepts as input changed.";

    public ConnectionToolSummary Tool => tool;

    public string Title => string.IsNullOrWhiteSpace(tool.Title) ? tool.Name : tool.Title;

    public string Name => tool.Name;

    public string Description => tool.Description;

    public bool HasDescription => tool.Description.Length > 0;

    public bool Missing => tool.State is "missing";

    public bool NeedsReview => tool.NeedsReview;

    public bool ShowCompare => tool.NeedsReview && tool.Reviewed is not null;

    /// <summary>What the reviewed definition said: its title, then its words.</summary>
    public string ReviewedWords => tool.Reviewed is { } reviewed
        ? $"{(string.IsNullOrWhiteSpace(reviewed.Title) ? tool.Name : reviewed.Title)}\n{reviewed.Description}"
        : string.Empty;

    public string NowWords => $"{Title}\n{tool.Description}";

    public bool ShowSameWords => tool.NeedsReview && tool.Reviewed is { } reviewed
        && reviewed.Title == tool.Title && reviewed.Description == tool.Description;

    public bool ShowAccept => tool.NeedsReview && canEdit;

    public string AcceptLabel => $"Accept the change to {Title}";

    #endregion

    #region Public Methods

    [RelayCommand]
    public Task Accept() => card.Accept(tool);

    #endregion
}
