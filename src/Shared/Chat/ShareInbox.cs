namespace Drover.Ui.Chat;

/// <summary>What another app shared to Drover: words, files, or both. Nothing of it has been sent anywhere.</summary>
public sealed record SharedDraft(string Text, IReadOnlyList<IBrowserFile> Files);

/// <summary>
/// Holds what another app shared until the person says which bot's conversation gets it. A native shell puts the
/// share in; the share screen names the bot; that bot's main conversation takes it as a draft, once. The web app
/// never puts anything in.
/// </summary>
public sealed class ShareInbox
{
    #region Private Fields

    private readonly Lock gate = new();

    private SharedDraft? waiting;

    private Guid? botId;

    #endregion

    #region Properties

    /// <summary>The share nobody has taken yet.</summary>
    public SharedDraft? Waiting
    {
        get
        {
            lock (gate)
            {
                return waiting;
            }
        }
    }

    #endregion

    #region Public Methods

    /// <summary>A new share takes the place of one that was never placed.</summary>
    public void Offer(SharedDraft draft)
    {
        lock (gate)
        {
            (waiting, botId) = (draft, null);
        }
    }

    /// <summary>The person chose the bot whose main conversation gets the share.</summary>
    public void SendTo(Guid bot)
    {
        lock (gate)
        {
            botId = waiting is null ? null : bot;
        }
    }

    /// <summary>Hands the share to the chosen bot's conversation, once. Any other conversation gets nothing.</summary>
    public SharedDraft? TakeFor(Guid bot)
    {
        lock (gate)
        {
            if (waiting is not { } draft || botId != bot)
            {
                return null;
            }

            (waiting, botId) = (null, null);
            return draft;
        }
    }

    public void Discard()
    {
        lock (gate)
        {
            (waiting, botId) = (null, null);
        }
    }

    #endregion
}
