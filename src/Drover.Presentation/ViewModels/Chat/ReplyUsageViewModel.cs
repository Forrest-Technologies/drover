namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// What a finished reply used, behind the reply's one action "Usage for this reply" and behind the status line above
/// the message field: the model that wrote it and, once read, the tokens and the cost, shown in a popover. The status
/// line reads the newest reply's once, when it first shows that reply (<see cref="ConversationStatusViewModel"/>); a
/// reply's own action reads only when pressed, so a conversation with many replies makes no request for them.
/// </summary>
public sealed partial class ReplyUsageViewModel(Guid runId, Func<Guid, Task<RunUsageDetail>> load, ILogger logger, string? model = null)
    : ObservableObject
{
    #region Private Fields

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lines), nameof(IsAdding))]
    private bool open;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lines), nameof(IsAdding))]
    private RunUsageDetail? detail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lines), nameof(IsAdding))]
    private string? problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WrittenBy))]
    private string? model = model;

    #endregion

    #region Properties

    /// <summary>The name of the action on a reply, and the title of what it opens.</summary>
    public static string Label => "Usage for this reply";

    /// <summary>Said, politely, while the server adds it up.</summary>
    public static string Adding => "Adding it up.";

    public bool IsAdding => Open && Problem is null && Detail is null;

    /// <summary>The model that wrote the reply, in words, when the server kept it.</summary>
    public string? WrittenBy => Model is null ? null : $"Written by {Model}";

    /// <summary>The one or two lines that say what the reply used, once it is known.</summary>
    public IReadOnlyList<string> Lines => Open && Problem is null && Detail is { } read ? UsageWords.Reply(read) : [];

    #endregion

    #region Public Methods

    [RelayCommand]
    public async Task Toggle()
    {
        Open = !Open;

        if (Open && Detail is null)
        {
            await Read();
        }
    }

    [RelayCommand]
    public async Task Read()
    {
        Problem = null;

        try
        {
            Detail = await load(runId);
        }
        catch (Exception exception) when (Failures.IsReportable(exception))
        {
            logger.LogWarning(exception, "Could not read what run {RunId} used", runId);
            Problem = $"Drover could not load usage. {Failures.Sentence(exception)}";
        }
    }

    #endregion
}
