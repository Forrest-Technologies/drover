namespace Drover.Presentation.ViewModels.Chat;

/// <summary>
/// The one quiet line under a conversation's composer: what the bot is doing now, in words, and the model that wrote
/// the newest finished reply with what that reply used. What it used is read once, when the reply finishes, so the
/// line makes one request for each finished run and none for an older reply. A read that fails leaves the figures
/// out; the popover the line opens says what failed. With nothing to say the line is not there.
/// </summary>
public sealed class ConversationStatusViewModel : ObservableObject
{
    #region Private Fields

    private Guid? run;

    private string? model;

    #endregion

    #region Properties

    /// <summary>What the bot is doing now, or nothing at rest.</summary>
    public string? State { get; private set; }

    /// <summary>The newest finished reply's model and figures, when there is one this person may read.</summary>
    public ReplyUsageViewModel? Usage { get; private set; }

    /// <summary>False when the line has nothing to say: it then takes no room.</summary>
    public bool HasAny => State is not null || Usage is not null;

    #endregion

    #region Public Methods

    /// <summary>Says what the conversation now holds. A reply that just finished has what it used read, once.</summary>
    public void Show(IReadOnlyList<ChatMessage> messages, bool thinking, Func<Guid, Task<RunUsageDetail>>? load, ILogger logger)
    {
        State = ChatRows.LiveState(messages, thinking);
        var reply = ChatRows.StatusReply(messages, load is not null);

        if (reply?.RunId != run || reply?.Model != model)
        {
            var read = reply?.RunId != run;
            var before = Usage?.Detail;
            (run, model) = (reply?.RunId, reply?.Model);
            Usage = reply is { RunId: { } runId } && load is not null ? new(runId, load, logger, reply.Model) { Detail = read ? null : before } : null;

            if (read && Usage is { } usage)
            {
                _ = usage.Read();
            }
        }

        OnPropertyChanged(string.Empty);
    }

    #endregion
}
