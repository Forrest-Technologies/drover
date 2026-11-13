namespace Drover.Engine.Runs;

/// <summary>
/// One watcher's view of a run tree's events: everything after a number, each event once and in order, to the end of
/// the run. Stored events are replayed from the table in pages, each page in its own short tenant transaction, so no
/// transaction lives as long as a stream. Then the live feed takes over. Events are gapless, so the handover is a
/// check that the next live event is the last one plus one; on a gap, or when nothing arrives for a heartbeat, the
/// table is read again. That same read is how a watcher on one server sees events another server wrote.
/// </summary>
public sealed class RunEventFeed(RunScopes scopes, RunEventHub hub, IOptions<RunOptions> options, TimeProvider time)
{
    #region Private Fields

    private const int PageSize = 200;

    #endregion

    #region Public Methods

    /// <summary>
    /// True when the person may watch the run: it is a root run of this space and its thread is one they may see.
    /// Row-level security answers both, so a private thread's run is simply not there for anyone else.
    /// </summary>
    public Task<bool> CanWatch(Guid spaceId, Guid userId, Guid rootRunId, CancellationToken cancellationToken) =>
        scopes.For(spaceId, userId).Transact(
            async services =>
            {
                var run = await services.GetRequiredService<RunRepository>().FindRun(rootRunId, cancellationToken);
                return run is not null && run.RootRunId == run.Id
                    && await services.GetRequiredService<ThreadRepository>().FindThread(run.ThreadId, cancellationToken) is not null;
            },
            cancellationToken);

    /// <summary>
    /// The events after <paramref name="afterSeq"/>. A null item is a heartbeat: nothing happened for a while, and
    /// the connection should be kept warm. The sequence ends when the root run has ended and everything is sent.
    /// </summary>
    public async IAsyncEnumerable<RunEventResponse?> Watch(
        Guid spaceId,
        Guid userId,
        Guid rootRunId,
        long afterSeq,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var tenant = scopes.For(spaceId, userId);
        var last = afterSeq;

        // Subscribed before the first read, so nothing committed between the read and the live feed can be missed.
        using var live = hub.Subscribe(rootRunId);
        Task<bool>? waiting = null;

        while (true)
        {
            while (true)
            {
                var page = await ReadPage(tenant, rootRunId, last, cancellationToken);

                foreach (var stored in page.Events)
                {
                    last = stored.Seq;
                    yield return stored;
                }

                if (page.RunIsOver && last >= page.LastEventSeq)
                {
                    yield break;
                }

                if (page.Events.Count < PageSize)
                {
                    break;
                }
            }

            while (true)
            {
                waiting ??= live.Events.WaitToReadAsync(cancellationToken).AsTask();
                var first = await Task.WhenAny(waiting, Task.Delay(options.Value.HeartbeatInterval, time, cancellationToken));

                if (first != waiting)
                {
                    yield return null;
                    break;
                }

                var open = await waiting;
                waiting = null;

                if (!open || !live.Events.TryRead(out var next) || next.Seq <= last)
                {
                    continue;
                }

                if (next.Seq != last + 1)
                {
                    break;
                }

                last = next.Seq;
                yield return next;

                if (next.RunId == rootRunId && RunEventTypes.IsTerminal(next.Type))
                {
                    yield break;
                }
            }
        }
    }

    #endregion

    #region Private Methods

    private static Task<Page> ReadPage(RunTenant tenant, Guid rootRunId, long afterSeq, CancellationToken cancellationToken) =>
        tenant.Transact(
            async services =>
            {
                var events = await services.GetRequiredService<RunEventRepository>().ListEvents(rootRunId, afterSeq, PageSize, cancellationToken);
                var run = await services.GetRequiredService<RunRepository>().FindRun(rootRunId, cancellationToken);
                return new Page(
                    [.. events.Select(stored => stored.ToResponse())],
                    run is null or { Status: RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled },
                    run?.LastEventSeq ?? 0);
            },
            cancellationToken);

    #endregion

    #region Nested Types

    private sealed record Page(IReadOnlyList<RunEventResponse> Events, bool RunIsOver, long LastEventSeq);

    #endregion
}
