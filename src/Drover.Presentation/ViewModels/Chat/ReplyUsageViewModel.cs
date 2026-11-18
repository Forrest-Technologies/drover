namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// What a finished reply used, behind one small control under its words. Read only when asked for: a conversation
/// with many replies makes no request until a person opens one.
/// </summary>
public sealed partial class ReplyUsageViewModel(Guid runId, Func<Guid, Task<RunUsageDetail>> load, ILogger logger) : ObservableObject
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

    #endregion

    #region Properties

    public static string Label => "What this reply used";

    /// <summary>Said, politely, while the server adds it up.</summary>
    public static string Adding => "Adding it up.";

    public bool IsAdding => Open && Problem is null && Detail is null;

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
            Problem = Failures.Sentence(exception);
        }
    }

    #endregion
}
