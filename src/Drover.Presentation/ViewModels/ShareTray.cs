namespace Drover.Presentation.ViewModels;

/// <summary>A file another app shared: its name and its size. Its content stays with the shell that received it.</summary>
public sealed record ShareFile(string Name, long Size);

/// <summary>What another app shared to Drover: words, files, or both. Nothing of it has been sent anywhere.</summary>
public sealed record SharedContent(string Text, IReadOnlyList<ShareFile> Files);

/// <summary>
/// Holds what another app shared until the person says which bot's conversation gets it. A native shell puts the
/// share in; the share screen names the bot; that bot's main conversation takes it as a draft, once.
/// </summary>
public sealed class ShareTray
{
    #region Private Fields

    private readonly Lock gate = new();

    private SharedContent? waiting;

    private Guid? botId;

    #endregion

    #region Properties

    /// <summary>The share nobody has taken yet.</summary>
    public SharedContent? Waiting
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
    public void Offer(SharedContent content)
    {
        lock (gate)
        {
            (waiting, botId) = (content, null);
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
    public SharedContent? TakeFor(Guid bot)
    {
        lock (gate)
        {
            if (waiting is not { } content || botId != bot)
            {
                return null;
            }

            (waiting, botId) = (null, null);
            return content;
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
