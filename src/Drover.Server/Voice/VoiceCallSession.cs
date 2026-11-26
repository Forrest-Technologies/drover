namespace Drover.Server.Voice;

/// <summary>
/// One live call between a person's socket and a speech-to-speech model. Three things run side by side: the
/// person's audio and commands go up to the model, the model's audio and words come down, and one sender writes
/// to the socket from a bounded queue. Whichever of them finishes first ends the call. Audio passes through and is
/// never kept; the words are added to the conversation after each exchange, in a short transaction of their own.
/// </summary>
public sealed class VoiceCallSession(
    CallPlan plan,
    IRealtimeVoiceSession model,
    WebSocket socket,
    VoiceService voice,
    TimeProvider time,
    ILogger logger)
{
    #region Private Fields

    /// <summary>About five seconds of audio. A client sends far smaller frames; a larger one is not audio from a microphone.</summary>
    public const int MaxAudioFrameBytes = 256 * 1024;

    public const int MaxTextFrameBytes = 1024;

    /// <summary>How many frames may wait for a slow client before the call is dropped instead of held in memory.</summary>
    public const int MaxQueuedFrames = 512;

    public const string RunStarted = "The bot started working in this conversation, so the call ended.";

    public const string ClientTooSlow = "The connection could not keep up with the call, so it ended.";

    public const string FrameTooLarge = "The call received something that is not microphone audio, so it ended.";

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan closeWait = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan limitCheck = TimeSpan.FromSeconds(1);

    private readonly Channel<Frame> outgoing = Channel.CreateBounded<Frame>(
        new BoundedChannelOptions(MaxQueuedFrames) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    // Written by whichever side ends the call first. The first reason given stands.
    private string? endReason;

    private string? failure;

    private string? personWaiting;

    private string? botWaiting;

    private long inputTokens;

    private long outputTokens;

    private int exchanges;

    #endregion

    #region Public Methods

    /// <summary>
    /// Runs the call until someone hangs up, the provider ends it, the time limit is reached, or
    /// <paramref name="cancellationToken"/> fires (the client went away, the server is stopping, the session was
    /// signed out). Then writes what was said last, says why the call ended, and closes the socket.
    /// </summary>
    public async Task Run(CancellationToken stopping, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        using var running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Queue(new VoiceCallFrame(VoiceCallMessages.Ready) { Model = plan.Model, Voice = plan.Voice, MaxMinutes = plan.MaxMinutes });
        using var sendStop = new CancellationTokenSource();
        var sending = Send(sendStop.Token);
        Task[] sides = [Upstream(running.Token), Downstream(running.Token), Limit(running.Token)];

        try
        {
            await await Task.WhenAny(sides);
        }
        catch (OperationCanceledException stopped)
        {
            logger.LogDebug("The live call in thread {ThreadId} was stopped ({Reason})", plan.ThreadId, stopped.GetType().Name);
            End(stopping.IsCancellationRequested ? VoiceCallEndReasons.Server : VoiceCallEndReasons.HungUp);
        }
        catch (Exception problem) when (problem is WebSocketException or IOException)
        {
            logger.LogInformation("The live call in thread {ThreadId} lost its connection ({Reason})", plan.ThreadId, problem.GetType().Name);
            End(VoiceCallEndReasons.HungUp);
        }
        catch (ModelProviderException problem)
        {
            logger.LogWarning("The live call in thread {ThreadId} failed at the provider: {Failure}", plan.ThreadId, problem.Failure);
            failure ??= problem.Message;
            End(VoiceCallEndReasons.Provider);
        }

        await running.CancelAsync();
        await Settle(sides);
        await Finish(sending, sendStop);
        logger.LogInformation(
            "Live call in thread {ThreadId} ended ({Reason}) after {Seconds} s and {Exchanges} exchanges; {InputTokens} tokens in, {OutputTokens} out",
            plan.ThreadId,
            endReason,
            (long)time.GetElapsedTime(started).TotalSeconds,
            exchanges,
            inputTokens,
            outputTokens);
    }

    #endregion

    #region The Three Sides

    /// <summary>The person's side: microphone audio as binary frames, and interrupt or hang up as JSON.</summary>
    private async Task Upstream(CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxAudioFrameBytes];

        while (true)
        {
            var filled = 0;
            ValueWebSocketReceiveResult received;

            do
            {
                if (filled == buffer.Length)
                {
                    failure = FrameTooLarge;
                    End(VoiceCallEndReasons.Server);
                    return;
                }

                received = await socket.ReceiveAsync(buffer.AsMemory(filled), cancellationToken);
                filled += received.Count;
            }
            while (!received.EndOfMessage);

            if (received.MessageType is WebSocketMessageType.Close)
            {
                End(VoiceCallEndReasons.HungUp);
                return;
            }

            if (received.MessageType is WebSocketMessageType.Binary)
            {
                if (filled > 0)
                {
                    await model.SendAudio(buffer.AsMemory(0, filled), cancellationToken);
                }

                continue;
            }

            if (filled > MaxTextFrameBytes || ReadCommand(buffer.AsSpan(0, filled)) is not { } command)
            {
                continue;
            }

            if (command.Type is VoiceCallMessages.Hangup)
            {
                End(VoiceCallEndReasons.HungUp);
                return;
            }

            if (command.Type is VoiceCallMessages.Interrupt)
            {
                await model.Interrupt(Math.Max(0, command.HeardMs ?? 0), cancellationToken);
            }
        }
    }

    /// <summary>The model's side: its audio goes straight down, its words go down and into the conversation.</summary>
    private async Task Downstream(CancellationToken cancellationToken)
    {
        await foreach (var happened in model.Read(cancellationToken))
        {
            var goesOn = happened switch
            {
                RealtimeAudioOut audio => Queue(new Frame(audio.Samples, Binary: true)),
                RealtimePersonStartedSpeaking => await PersonStarted(cancellationToken),
                RealtimePersonSaid said => await PersonSaid(said.Text, cancellationToken),
                RealtimeBotSaying saying => Queue(new VoiceCallFrame(VoiceCallMessages.BotSaying) { Text = saying.Text }),
                RealtimeBotSaid said => await BotSaid(said.Text, cancellationToken),
                RealtimeTurnUsage usage => Count(usage),
                RealtimeFailed failed => Queue(new VoiceCallFrame(VoiceCallMessages.Failed) { Message = failed.Message }),
                _ => true,
            };

            if (!goesOn)
            {
                return;
            }
        }

        End(VoiceCallEndReasons.Provider);
    }

    /// <summary>
    /// Ends the call at the server's limit, when it has one. The clock is read once a second, so the limit follows
    /// the server's clock and not a timer set when the call began.
    /// </summary>
    private async Task Limit(CancellationToken cancellationToken)
    {
        if (plan.MaxMinutes is not { } minutes)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        var began = time.GetUtcNow();
        using var tick = new PeriodicTimer(limitCheck);

        while (time.GetUtcNow() - began < TimeSpan.FromMinutes(minutes))
        {
            await tick.WaitForNextTickAsync(cancellationToken);
        }

        End(VoiceCallEndReasons.TimeLimit);
    }

    /// <summary>The only writer to the socket.</summary>
    private async Task Send(CancellationToken cancellationToken)
    {
        await foreach (var frame in outgoing.Reader.ReadAllAsync(cancellationToken))
        {
            await socket.SendAsync(
                frame.Bytes,
                frame.Binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
    }

    #endregion

    #region Words

    /// <summary>
    /// A reply still waiting for the words it answered is written now: the person is speaking again, so those words
    /// are not coming.
    /// </summary>
    private async Task<bool> PersonStarted(CancellationToken cancellationToken) =>
        Queue(new VoiceCallFrame(VoiceCallMessages.PersonStarted)) && await Write(null, Take(ref botWaiting), cancellationToken);

    /// <summary>
    /// Some providers write down what the person said only after the bot has answered it. A reply that is waiting
    /// is that answer, so the two are written together in the order they were spoken.
    /// </summary>
    private async Task<bool> PersonSaid(string text, CancellationToken cancellationToken)
    {
        if (!Queue(new VoiceCallFrame(VoiceCallMessages.PersonSaid) { Text = text }))
        {
            return false;
        }

        if (botWaiting is not null)
        {
            return await Write(text, Take(ref botWaiting), cancellationToken);
        }

        personWaiting = personWaiting is null ? text : $"{personWaiting} {text}";
        return true;
    }

    private async Task<bool> BotSaid(string text, CancellationToken cancellationToken)
    {
        if (!Queue(new VoiceCallFrame(VoiceCallMessages.BotSaid) { Text = text }))
        {
            return false;
        }

        if (personWaiting is not null)
        {
            return await Write(Take(ref personWaiting), text, cancellationToken);
        }

        // An earlier reply that never got its words is written before this one takes its place.
        var written = await Write(null, Take(ref botWaiting), cancellationToken);
        botWaiting = text;
        return written;
    }

    /// <summary>False when the conversation can no longer take the call's words, which ends the call.</summary>
    private async Task<bool> Write(string? person, string? bot, CancellationToken cancellationToken)
    {
        if (person is null && bot is null)
        {
            return true;
        }

        var written = await voice.RecordExchange(plan, person, bot, cancellationToken);

        if (written is CallWrite.Written)
        {
            exchanges++;
            return true;
        }

        failure = written is CallWrite.RunActive ? RunStarted : null;
        End(VoiceCallEndReasons.Server);
        return false;
    }

    private bool Count(RealtimeTurnUsage usage)
    {
        inputTokens += usage.InputTokens;
        outputTokens += usage.OutputTokens;
        return true;
    }

    private static string? Take(ref string? waiting)
    {
        var taken = waiting;
        waiting = null;
        return taken;
    }

    #endregion

    #region Ending

    private void End(string reason) => Interlocked.CompareExchange(ref endReason, reason, null);

    /// <summary>Waits for the sides to stop. Each was cancelled, so what they throw now is only that.</summary>
    private async Task Settle(Task[] sides)
    {
        foreach (var side in sides)
        {
            try
            {
                await side;
            }
            catch (Exception stopped) when (stopped is OperationCanceledException or WebSocketException or IOException or ModelProviderException)
            {
                logger.LogDebug("A side of the live call in thread {ThreadId} stopped ({Reason})", plan.ThreadId, stopped.GetType().Name);
            }
        }
    }

    /// <summary>
    /// Writes the words still waiting, tells the client why the call ended, and closes the socket. The caller's
    /// token is already cancelled, so this runs on a short time limit of its own: what a person said on a call that
    /// was cut off is still theirs to keep.
    /// </summary>
    private async Task Finish(Task sending, CancellationTokenSource sendStop)
    {
        using var closing = new CancellationTokenSource(closeWait, time);

        try
        {
            // What is already queued (the last words) is still sent, for as long as the closing time allows.
            outgoing.Writer.TryComplete();
            sendStop.CancelAfter(closeWait);
            await sending;
        }
        catch (Exception stopped) when (stopped is OperationCanceledException or WebSocketException or IOException)
        {
            logger.LogDebug("The sender of the live call in thread {ThreadId} stopped ({Reason})", plan.ThreadId, stopped.GetType().Name);
        }

        try
        {
            var kept = await voice.RecordExchange(plan, Take(ref personWaiting), Take(ref botWaiting), closing.Token);
            exchanges += kept is CallWrite.Written ? 1 : 0;

            if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            {
                return;
            }

            if (failure is not null)
            {
                await SendNow(new VoiceCallFrame(VoiceCallMessages.Failed) { Message = failure }, closing.Token);
            }

            await SendNow(new VoiceCallFrame(VoiceCallMessages.Ended) { Reason = endReason ?? VoiceCallEndReasons.Server }, closing.Token);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closing.Token);
        }
        catch (Exception stopped) when (stopped is OperationCanceledException or WebSocketException or IOException)
        {
            logger.LogInformation("The live call in thread {ThreadId} could not be closed cleanly ({Reason})", plan.ThreadId, stopped.GetType().Name);
        }
    }

    private Task SendNow(VoiceCallFrame frame, CancellationToken cancellationToken) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(frame, json), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    #endregion

    #region Frames

    private bool Queue(VoiceCallFrame frame) => Queue(new Frame(JsonSerializer.SerializeToUtf8Bytes(frame, json), Binary: false));

    /// <summary>False when the client has fallen so far behind that the queue is full: the call is dropped, not buffered.</summary>
    private bool Queue(Frame frame)
    {
        if (outgoing.Writer.TryWrite(frame))
        {
            return true;
        }

        failure = ClientTooSlow;
        End(VoiceCallEndReasons.Server);
        return false;
    }

    /// <summary>A frame that is not one of the two commands is ignored, as an unknown command is.</summary>
    private VoiceCallFrame? ReadCommand(ReadOnlySpan<byte> text)
    {
        try
        {
            return JsonSerializer.Deserialize<VoiceCallFrame>(text, json);
        }
        catch (JsonException unreadable)
        {
            logger.LogDebug("The live call in thread {ThreadId} ignored a text frame ({Reason})", plan.ThreadId, unreadable.GetType().Name);
            return null;
        }
    }

    #endregion

    #region Nested Types

    private readonly record struct Frame(ReadOnlyMemory<byte> Bytes, bool Binary);

    #endregion
}
