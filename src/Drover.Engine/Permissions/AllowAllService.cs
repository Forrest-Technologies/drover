namespace Drover.Engine.Permissions;

/// <summary>
/// Opens, ends, and finds allow-all windows. Every method runs in the caller's tenant scope: the caller owns and
/// commits the transaction, so a window and the change that ends it (a run finishing, a bot paused, a person
/// leaving) are written together. All times come from the server's clock, never from a client or a model.
/// </summary>
public sealed class AllowAllService(
    AllowAllWindowRepository windows,
    RunRepository runs,
    AuditLog audit,
    TimeProvider time,
    ILogger<AllowAllService> logger)
{
    #region Public Methods

    /// <summary>
    /// Opens a window for one bot and person, ending the open one first. Exactly one window stays open for the pair:
    /// the space's lock serializes two openings, and the partial unique index is the last word.
    /// </summary>
    public async Task<AllowAllWindow> Open(
        Guid botId,
        Guid audienceUserId,
        AllowAllWindowKind kind,
        AllowAllWindowSource source,
        Guid? toolCallId,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        if (source is AllowAllWindowSource.Approval && toolCallId is null)
        {
            throw new ArgumentException("A window started from an approval card must name the call it came from.", nameof(toolCallId));
        }

        var now = time.GetUtcNow();
        await windows.LockSpaceWindows(cancellationToken);

        if (await windows.FindOpen(botId, audienceUserId, cancellationToken) is { } previous)
        {
            End(previous, HasExpired(previous, now) ? AllowAllWindowEndReason.Expired : AllowAllWindowEndReason.AskedAgain, actorUserId);
        }

        var window = new AllowAllWindow
        {
            BotId = botId,
            AudienceUserId = audienceUserId,
            Kind = kind,
            StartedAt = now,
            EndsAt = EndsAt(kind, now),
            StartedFrom = source,
            ToolCallId = source is AllowAllWindowSource.Approval ? toolCallId : null,
        };
        windows.Add(window);

        audit.Record(
            actorUserId,
            StartedAction,
            AuditTargets.AllowAllWindow,
            window.Id,
            null,
            null,
            AuditLog.Snapshot(new
            {
                kind = AllowAllWindowKindNames.Of(kind),
                window.EndsAt,
                started_from = source,
                window.ToolCallId,
            }));
        logger.LogInformation(
            "Allow-all window {WindowId} opened for bot {BotId} by person {UserId} with kind {Kind}",
            window.Id,
            botId,
            actorUserId,
            kind);
        return window;
    }

    /// <summary>
    /// Ends a window the caller has already locked. A second end writes nothing: the caller reads with
    /// <c>FOR UPDATE</c>, so another ending waits for this transaction and then sees the row as ended.
    /// </summary>
    private bool End(AllowAllWindow window, AllowAllWindowEndReason reason, Guid actorUserId)
    {
        if (window.EndedAt is not null)
        {
            return false;
        }

        var now = time.GetUtcNow();
        window.EndedAt = now;
        window.EndedReason = reason;

        audit.Record(
            actorUserId,
            EndedAction,
            AuditTargets.AllowAllWindow,
            window.Id,
            null,
            null,
            AuditLog.Snapshot(new
            {
                ended_at = now,
                ended_reason = reason,
                kind = AllowAllWindowKindNames.Of(window.Kind),
                window.BotId,
                audience_user_id = window.AudienceUserId,
            }));
        logger.LogInformation(
            "Allow-all window {WindowId} ended with reason {Reason}",
            window.Id,
            reason);
        return true;
    }

    /// <summary>Ends a window by id, for the API's Ask again. False when it is not open or not visible in this space.</summary>
    public async Task<bool> End(Guid windowId, AllowAllWindowEndReason reason, Guid actorUserId, CancellationToken cancellationToken)
    {
        var window = await windows.FindOpenById(windowId, cancellationToken);
        return window is not null && End(window, reason, actorUserId);
    }

    /// <summary>
    /// Ends the open windows of a bot that the caller can see, when it is paused or removed. Row-level security shows
    /// a caller only their own audience, so each person's window is ended by a transaction running for that person;
    /// a window no longer visible to its person is never approved at the gate.
    /// </summary>
    public async Task EndForBot(Guid botId, AllowAllWindowEndReason reason, Guid actorUserId, CancellationToken cancellationToken)
    {
        foreach (var window in await windows.ListOpenForBot(botId, cancellationToken))
        {
            End(window, reason, actorUserId);
        }
    }

    /// <summary>
    /// Ends the open windows addressed to one person, when they leave. Called in the leaving person's scope, which
    /// is the scope that can see those rows.
    /// </summary>
    public async Task EndForAudience(Guid audienceUserId, AllowAllWindowEndReason reason, Guid actorUserId, CancellationToken cancellationToken)
    {
        foreach (var window in await windows.ListOpenForAudience(audienceUserId, cancellationToken))
        {
            End(window, reason, actorUserId);
        }
    }

    /// <summary>
    /// The open window for a bot and person at this instant, by the server clock. A timed window whose end has
    /// passed is ended with reason <c>expired</c> and is no longer returned, so the first ask after it waits again.
    /// </summary>
    public async Task<AllowAllWindow?> FindOpen(Guid botId, Guid audienceUserId, CancellationToken cancellationToken)
    {
        var window = await windows.FindOpen(botId, audienceUserId, cancellationToken);

        if (window is not null && HasExpired(window, time.GetUtcNow()))
        {
            End(window, AllowAllWindowEndReason.Expired, audienceUserId);
            return null;
        }

        return window;
    }

    /// <summary>
    /// Ends an until-done window once the bot has nothing queued, running, or waiting for its person. Call in the
    /// same transaction that ends the last run. A window of another kind is left alone.
    /// </summary>
    public async Task EndUntilDoneIfIdle(Guid botId, Guid audienceUserId, CancellationToken cancellationToken)
    {
        var window = await windows.FindOpen(botId, audienceUserId, cancellationToken);

        if (window is { Kind: AllowAllWindowKind.UntilDone }
            && !await runs.AnyRunWorkingFor(botId, audienceUserId, cancellationToken))
        {
            End(window, AllowAllWindowEndReason.WorkDone, audienceUserId);
        }
    }

    #endregion

    #region Private Methods

    private static DateTimeOffset? EndsAt(AllowAllWindowKind kind, DateTimeOffset startedAt) =>
        kind switch
        {
            AllowAllWindowKind.Minutes30 => startedAt.AddMinutes(30),
            AllowAllWindowKind.Hour1 => startedAt.AddHours(1),
            _ => null,
        };

    private static bool HasExpired(AllowAllWindow window, DateTimeOffset now) =>
        window.EndsAt is { } endsAt && now >= endsAt;

    #endregion

    #region Constants

    public const string StartedAction = "allow_all.started";

    public const string EndedAction = "allow_all.ended";

    #endregion
}
