namespace Drover.Engine.Runs;

/// <summary>
/// Writes one run tree's events, one writer at a time, and publishes them after they commit. Model output arrives
/// token by token, so deltas are coalesced: a chunk is written when it reaches the size limit or the flush interval
/// passes, whichever comes first, and always before any other event, so the log keeps the order things happened in.
/// What is published is exactly what was stored: live and replay are the same bytes.
/// </summary>
public sealed class RunEventWriter(
    RunTenant tenant,
    Guid rootRunId,
    ChatThread thread,
    RunEventHub hub,
    RunOptions options,
    TimeProvider time,
    ILogger logger) : IAsyncDisposable
{
    #region Private Fields

    private readonly SemaphoreSlim gate = new(1, 1);

    private readonly CancellationTokenSource stopping = new();

    private DeltaBuffer? buffer;

    private Task flushing = Task.CompletedTask;

    private Guid lastMessageId;

    private string? lastKind;

    private int partIndex;

    #endregion

    #region Public Methods

    public static NewRunEvent Event(Guid runId, string type, object payload) =>
        new(runId, type, JsonElement.Parse(RunEventTypes.Write(payload)));

    /// <summary>Adds text to the part being streamed. A change of message or of kind starts a new part.</summary>
    public async Task Delta(Guid runId, Guid messageId, string kind, string text)
    {
        await gate.WaitAsync();

        try
        {
            if (buffer is { } current && (current.MessageId != messageId || current.Kind != kind))
            {
                await WriteLocked(null, []);
            }

            buffer ??= StartBuffer(runId, messageId, kind);
            buffer.Text.Append(text);

            if (buffer.Text.Length >= options.DeltaFlushCharacters)
            {
                await WriteLocked(null, []);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public Task Append(Guid runId, string type, object payload) => Write(null, [Event(runId, type, payload)]);

    /// <summary>
    /// Writes the buffered delta and then the events, in one transaction. <paramref name="alongside"/> runs in that
    /// transaction first, so a status change and the event that reports it commit together or not at all.
    /// </summary>
    public async Task Write(Func<IServiceProvider, Task>? alongside, IReadOnlyList<NewRunEvent> events)
    {
        await gate.WaitAsync();

        try
        {
            await WriteLocked(alongside, events);
        }
        finally
        {
            gate.Release();
        }
    }

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        await flushing;
        stopping.Dispose();
        gate.Dispose();
    }

    #endregion

    #region Private Methods

    private DeltaBuffer StartBuffer(Guid runId, Guid messageId, string kind)
    {
        if (messageId != lastMessageId)
        {
            partIndex = 0;
        }
        else if (kind != lastKind)
        {
            partIndex++;
        }

        lastMessageId = messageId;
        lastKind = kind;
        var started = new DeltaBuffer(runId, messageId, partIndex, kind);
        flushing = FlushLater(started);
        return started;
    }

    private async Task WriteLocked(Func<IServiceProvider, Task>? alongside, IReadOnlyList<NewRunEvent> events)
    {
        List<NewRunEvent> writing = [];

        if (buffer is { Text.Length: > 0 } pending)
        {
            writing.Add(Event(
                pending.RunId,
                RunEventTypes.MessageDelta,
                new MessageDelta(pending.MessageId, pending.PartIndex, pending.Kind, pending.Text.ToString())));
        }

        writing.AddRange(events);

        if (writing.Count == 0 && alongside is null)
        {
            return;
        }

        var stored = await tenant.Transact(
            async services =>
            {
                if (alongside is not null)
                {
                    await alongside(services);
                }

                return await services.GetRequiredService<RunEventRepository>()
                    .Append(rootRunId, thread, writing, CancellationToken.None);
            },
            CancellationToken.None);

        buffer = null;
        hub.Publish(rootRunId, stored);
    }

    /// <summary>Writes the chunk when the flush interval passes, unless something wrote it first.</summary>
    private async Task FlushLater(DeltaBuffer started)
    {
        try
        {
            await Task.Delay(options.DeltaFlushInterval, time, stopping.Token);
            await gate.WaitAsync(stopping.Token);

            try
            {
                if (ReferenceEquals(buffer, started))
                {
                    await WriteLocked(null, []);
                }
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogTrace("The event writer of run {RunId} stopped before a timed flush", rootRunId);
        }
        catch (Exception exception) when (exception is not ObjectDisposedException)
        {
            // The chunk stays buffered, so the next write carries it and reports the failure to the run.
            logger.LogWarning(exception, "A timed flush of run {RunId} failed; the next write retries it", rootRunId);
        }
        catch (ObjectDisposedException)
        {
            logger.LogTrace("The event writer of run {RunId} outlived its host", rootRunId);
        }
    }

    #endregion

    #region Nested Types

    private sealed class DeltaBuffer(Guid runId, Guid messageId, int partIndex, string kind)
    {
        #region Properties

        public Guid RunId => runId;

        public Guid MessageId => messageId;

        public int PartIndex => partIndex;

        public string Kind => kind;

        public StringBuilder Text { get; } = new();

        #endregion
    }

    #endregion
}
