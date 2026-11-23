namespace Drover.Ui.Chat;

/// <summary>What another app shared to Drover: words, files, or both. Nothing of it has been sent anywhere.</summary>
public sealed record SharedDraft(string Text, IReadOnlyList<IPickedFile> Files);

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

    private long generation;

    #endregion

    #region Properties

    /// <summary>
    /// The one inbox of the process. On a cold start Android can receive a share through services made before the
    /// interface has a window; the container the interface builds when it starts must read that same share.
    /// </summary>
    public static ShareInbox Process { get; } = new();

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
        SharedDraft? replaced;

        lock (gate)
        {
            replaced = waiting;
            (waiting, botId) = (draft, null);
        }

        Release(replaced);
    }

    /// <summary>
    /// Takes a share whose files are still being copied. The share is placed only if nobody signed in, signed out or
    /// changed server while it was copied: a share belongs to the person and server it arrived for. Otherwise it is
    /// dropped and its staged copies are released. Returns whether the share was placed.
    /// </summary>
    public async Task<bool> Receive(Func<Task<SharedDraft?>> stage)
    {
        var arrived = CurrentGeneration();
        var draft = await stage();

        return draft is not null && Place(draft, arrived);
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

    /// <summary>The person sent the share nowhere. Forgets the waiting share and releases its staged copies.</summary>
    public void Discard()
    {
        SharedDraft? dropped;

        lock (gate)
        {
            dropped = waiting;
            (waiting, botId) = (null, null);
        }

        Release(dropped);
    }

    /// <summary>
    /// Run when the person or the server changes. Forgets the waiting share, and ends every receive still copying one,
    /// so nothing that arrived for the earlier person or server can be placed for the next.
    /// </summary>
    public void Reset()
    {
        SharedDraft? dropped;

        lock (gate)
        {
            dropped = waiting;
            (waiting, botId) = (null, null);
            generation++;
        }

        Release(dropped);
    }

    #endregion

    #region Private Methods

    private long CurrentGeneration()
    {
        lock (gate)
        {
            return generation;
        }
    }

    private bool Place(SharedDraft draft, long arrived)
    {
        SharedDraft? displaced;
        bool placed;

        lock (gate)
        {
            placed = generation == arrived;
            displaced = placed ? waiting : draft;

            if (placed)
            {
                (waiting, botId) = (draft, null);
            }
        }

        Release(displaced);
        return placed;
    }

    private static void Release(SharedDraft? draft)
    {
        foreach (var file in draft?.Files.OfType<IDisposable>() ?? [])
        {
            file.Dispose();
        }
    }

    #endregion
}
