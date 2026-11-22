namespace Drover.Engine.Sandboxes;

/// <summary>The mouse buttons a person holds down on one bot's sandbox screen, and who pressed them.</summary>
internal sealed record HeldButtons(Guid SpaceId, Guid BotId, Guid UserId, IReadOnlyList<MouseButton> Buttons);


/// <summary>
/// The mouse buttons people hold down on bots' sandbox screens, so that every one is let go of. A held button is a
/// state of the sandbox's display that no connection owns: a person whose connection is lost, or whose request fails or
/// is cancelled, would leave it down for the next person or the bot to find. So a button is let go of when the request
/// that pressed it fails or is cancelled, when the sandbox is paused or removed, when this server stops, and when
/// <see cref="Limit"/> passes with no move or release for it. A release that fails is tried again after the same time,
/// up to <see cref="MaxFailedReleases"/> times in a row. Nothing here keeps a position: only which buttons are down,
/// for which bot and by whom.
/// </summary>
internal sealed class SandboxPointerHolds(TimeProvider time, ILogger logger) : IAsyncDisposable
{
    #region Private Fields

    private const int MaxFailedReleases = 6;

    private static readonly TimeSpan limit = TimeSpan.FromSeconds(10);

    private readonly Lock gate = new();

    private readonly Dictionary<(Guid SpaceId, Guid BotId), Hold> holds = [];

    private bool closed;

    #endregion

    #region Properties

    /// <summary>How long a held button may go without a move or a release before it is let go of.</summary>
    public static TimeSpan Limit => limit;

    #endregion

    #region Public Methods

    /// <summary>
    /// Notes that <paramref name="button"/> is about to go down, before the command that presses it runs, so that a
    /// command that fails or is cancelled halfway still has its button let go of. The time limit starts at
    /// <see cref="Keep"/>, once the press is known to have worked.
    /// </summary>
    public void Track(Guid spaceId, Guid botId, Guid userId, MouseButton button, Func<HeldButtons, CancellationToken, Task> release)
    {
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            if (!holds.TryGetValue((spaceId, botId), out var hold))
            {
                hold = new Hold(release);
                holds[(spaceId, botId)] = hold;
            }

            hold.UserId = userId;
            hold.Buttons.Add(button);
        }
    }

    /// <summary>
    /// The press worked: the button is held, which is noted again in case a release that began while the press ran let
    /// go of what was noted before it, and the time limit starts.
    /// </summary>
    public void Held(Guid spaceId, Guid botId, Guid userId, MouseButton button, Func<HeldButtons, CancellationToken, Task> release)
    {
        Track(spaceId, botId, userId, button, release);
        Keep(spaceId, botId);
    }

    /// <summary>Starts the time limit again for the buttons held on this bot's screen. Nothing happens when none is held.</summary>
    public void Keep(Guid spaceId, Guid botId)
    {
        lock (gate)
        {
            if (!closed && holds.TryGetValue((spaceId, botId), out var hold))
            {
                Arm(hold, spaceId, botId);
            }
        }
    }

    /// <summary>Forgets a button that was let go of. Nothing happens for one that was not held.</summary>
    public void Lift(Guid spaceId, Guid botId, MouseButton button)
    {
        lock (gate)
        {
            if (holds.TryGetValue((spaceId, botId), out var hold))
            {
                Forget(spaceId, botId, hold, [button]);
            }
        }
    }

    /// <summary>
    /// Lets go of every button held on this bot's screen and waits for it. A release that fails stays held, and the
    /// time limit starts again, so the next attempt is not far off.
    /// </summary>
    public async Task ReleaseAll(Guid spaceId, Guid botId, CancellationToken cancellationToken = default)
    {
        Hold? hold;
        HeldButtons held;

        lock (gate)
        {
            if (!holds.TryGetValue((spaceId, botId), out hold))
            {
                return;
            }

            hold.Timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            held = new HeldButtons(spaceId, botId, hold.UserId, [.. hold.Buttons]);
        }

        try
        {
            await hold.Release(held, cancellationToken);
        }
        catch (Exception failure)
        {
            logger.LogWarning("Letting go of a mouse button on the sandbox of bot {BotId} failed; the failure was {Failure}", botId, failure.GetType().Name);
            Fail(spaceId, botId, hold);
            return;
        }

        lock (gate)
        {
            Forget(spaceId, botId, hold, held.Buttons);
        }
    }

    public async ValueTask DisposeAsync()
    {
        List<(Guid SpaceId, Guid BotId)> held;

        lock (gate)
        {
            closed = true;
            held = [.. holds.Keys];
        }

        using var bound = new CancellationTokenSource(limit, time);

        foreach (var (spaceId, botId) in held)
        {
            await ReleaseAll(spaceId, botId, bound.Token);
        }

        lock (gate)
        {
            foreach (var hold in holds.Values)
            {
                hold.Timer?.Dispose();
            }

            holds.Clear();
        }
    }

    #endregion

    #region Private Methods

    /// <summary>Starts or restarts the time limit. Called with the gate held.</summary>
    private void Arm(Hold hold, Guid spaceId, Guid botId)
    {
        hold.Timer ??= time.CreateTimer(_ => Expire(spaceId, botId), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        hold.Timer.Change(limit, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The time limit passed: the buttons are let go of, in the background of the timer that ran out.</summary>
    private void Expire(Guid spaceId, Guid botId)
    {
        lock (gate)
        {
            if (closed || !holds.ContainsKey((spaceId, botId)))
            {
                return;
            }
        }

        logger.LogInformation("A mouse button held on the sandbox of bot {BotId} is being let go of after {Seconds} seconds", botId, limit.TotalSeconds);
        _ = ReleaseExpired(spaceId, botId);
    }

    private async Task ReleaseExpired(Guid spaceId, Guid botId)
    {
        try
        {
            await ReleaseAll(spaceId, botId);
        }
        catch (Exception failure)
        {
            logger.LogError("Letting go of a mouse button on the sandbox of bot {BotId} ended with {Failure}", botId, failure.GetType().Name);
        }
    }

    /// <summary>Drops the buttons from what is held, and the whole hold when none is left. Called with the gate held.</summary>
    private void Forget(Guid spaceId, Guid botId, Hold hold, IReadOnlyList<MouseButton> buttons)
    {
        hold.Buttons.ExceptWith(buttons);

        if (hold.Buttons.Count is 0)
        {
            hold.Timer?.Dispose();
            holds.Remove((spaceId, botId));
        }
    }

    /// <summary>A release failed: the buttons stay held and the limit starts again, until it has failed too many times.</summary>
    private void Fail(Guid spaceId, Guid botId, Hold hold)
    {
        lock (gate)
        {
            if (!holds.TryGetValue((spaceId, botId), out var current) || current != hold)
            {
                return;
            }

            hold.FailedReleases++;

            if (hold.FailedReleases >= MaxFailedReleases || closed)
            {
                logger.LogError("A mouse button held on the sandbox of bot {BotId} could not be let go of after {Attempts} tries", botId, hold.FailedReleases);
                hold.Timer?.Dispose();
                holds.Remove((spaceId, botId));
                return;
            }

            Arm(hold, spaceId, botId);
        }
    }

    #endregion

    #region Nested Types

    private sealed class Hold(Func<HeldButtons, CancellationToken, Task> release)
    {
        #region Properties

        public Func<HeldButtons, CancellationToken, Task> Release { get; } = release;

        public Guid UserId { get; set; }

        public HashSet<MouseButton> Buttons { get; } = [];

        public ITimer? Timer { get; set; }

        public int FailedReleases { get; set; }

        #endregion
    }

    #endregion
}
