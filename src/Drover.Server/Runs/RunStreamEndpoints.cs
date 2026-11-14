namespace Drover.Server.Runs;

/// <summary>
/// The two Server-Sent Events streams. Neither sits under the tenant scope filter: a stream lives for minutes, and
/// a transaction must not. The engine reads events in short scopes of its own.
/// </summary>
public static class RunStreamEndpoints
{
    #region Private Fields

    private const string LastEventIdHeader = "Last-Event-ID";

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private static readonly byte[] heartbeat = Encoding.UTF8.GetBytes(": keep-alive\n\n");

    #endregion

    #region Public Methods

    public static IEndpointRouteBuilder MapRunStreamEndpoints(this RouteGroupBuilder space)
    {
        space.MapGet("/runs/{runId:guid}/events", WatchRun)
            .WithName("WatchRun")
            .WithSummary("The events of a root run after a sequence number, as Server-Sent Events. Resumes from Last-Event-ID or ?after=.");
        space.MapGet("/stream", WatchSignals)
            .WithName("WatchSignals")
            .WithSummary("Hints for the signed-in person in this space: thread changed, run finished, approval waiting. No replay.");
        return space;
    }

    #endregion

    #region Endpoints

    private static async Task WatchRun(
        Guid runId,
        long? after,
        HttpContext context,
        RunEventFeed feed,
        IHostApplicationLifetime lifetime,
        ILoggerFactory loggers)
    {
        var access = context.GetSpaceAccess();
        var afterSeq = ReadCursor(context, after);

        if (afterSeq is null)
        {
            await TypedResults.Problem(title: "The cursor must be a sequence number of zero or more.", statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);
            return;
        }

        if (!await feed.CanWatch(access.SpaceId, access.UserId, runId, context.RequestAborted))
        {
            await TypedResults.Problem(statusCode: StatusCodes.Status404NotFound).ExecuteAsync(context);
            return;
        }

        await using var guard = SessionGuard.Start(context);
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            lifetime.ApplicationStopping,
            guard.Ended);
        await Stream(
            context,
            guard,
            feed.Watch(access.SpaceId, access.UserId, runId, afterSeq.Value, ending.Token),
            stored => $"id: {stored.Seq.ToString(CultureInfo.InvariantCulture)}\nevent: {stored.Type}\ndata: {JsonSerializer.Serialize(stored, json)}\n\n",
            loggers.CreateLogger(typeof(RunStreamEndpoints)),
            ending.Token);
    }

    private static async Task WatchSignals(
        HttpContext context,
        UserSignalHub hub,
        IOptions<RunOptions> options,
        TimeProvider time,
        IHostApplicationLifetime lifetime,
        ILoggerFactory loggers)
    {
        var access = context.GetSpaceAccess();
        await using var guard = SessionGuard.Start(context);
        using var ending = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted,
            lifetime.ApplicationStopping,
            guard.Ended);
        using var subscription = hub.Subscribe(access.SpaceId, access.UserId);
        await Stream(
            context,
            guard,
            ReadSignals(subscription, options.Value.HeartbeatInterval, time, ending.Token),
            signal => $"event: {signal.Kind}\ndata: {JsonSerializer.Serialize(signal, json)}\n\n",
            loggers.CreateLogger(typeof(RunStreamEndpoints)),
            ending.Token);
    }

    #endregion

    #region Private Methods

    /// <summary>The header a reconnecting client sends wins over the query, which is how a first connection names its start.</summary>
    private static long? ReadCursor(HttpContext context, long? after)
    {
        if (context.Request.Headers.TryGetValue(LastEventIdHeader, out var header) && header.Count > 0)
        {
            return long.TryParse(header.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var seen) ? seen : null;
        }

        return after is null or >= 0 ? after ?? 0 : null;
    }

    /// <summary>
    /// Writes each item as a frame and each null as a comment, which keeps proxies from closing a quiet stream. Before
    /// each write the guard may check the session; once the session is gone the stream ends and writes nothing more.
    /// </summary>
    private static async Task Stream<T>(
        HttpContext context,
        SessionGuard guard,
        IAsyncEnumerable<T?> items,
        Func<T, string> frame,
        ILogger logger,
        CancellationToken cancellationToken)
        where T : class
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await context.Response.Body.FlushAsync(cancellationToken);

            await foreach (var item in items)
            {
                if (!await guard.MayWrite(cancellationToken))
                {
                    logger.LogDebug("An event stream ended because its session ended");
                    break;
                }

                await context.Response.Body.WriteAsync(item is null ? heartbeat : Encoding.UTF8.GetBytes(frame(item)), cancellationToken);
                await context.Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("An event stream ended because the client left, its session ended, or the server is stopping");
        }
    }

    private static async IAsyncEnumerable<UserSignal?> ReadSignals(
        UserSignalSubscription subscription,
        TimeSpan heartbeatInterval,
        TimeProvider time,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Task<bool>? waiting = null;

        while (true)
        {
            waiting ??= subscription.Signals.WaitToReadAsync(cancellationToken).AsTask();
            var first = await Task.WhenAny(waiting, Task.Delay(heartbeatInterval, time, cancellationToken));

            if (first != waiting)
            {
                yield return null;
                continue;
            }

            var open = await waiting;
            waiting = null;

            if (!open)
            {
                yield break;
            }

            while (subscription.Signals.TryRead(out var signal))
            {
                yield return signal;
            }
        }
    }

    #endregion
}
