namespace Drover.Client;

/// <summary>Something that happened on a live call, as the client sees it.</summary>
public abstract record VoiceCallEvent;

/// <summary>The call is connected. <see cref="MaxMinutes"/> is null when the server sets no limit.</summary>
public sealed record VoiceCallReady(string Model, string Voice, int? MaxMinutes) : VoiceCallEvent;

/// <summary>The person started speaking. Audio still playing should stop, and <see cref="VoiceCall.Interrupt"/> says how much was heard.</summary>
public sealed record VoiceCallPersonStarted : VoiceCallEvent;

public sealed record VoiceCallPersonSaid(string Text) : VoiceCallEvent;

/// <summary>The next words of the answer being spoken.</summary>
public sealed record VoiceCallBotSaying(string Text) : VoiceCallEvent;

/// <summary>The whole answer, once spoken or cut off.</summary>
public sealed record VoiceCallBotSaid(string Text) : VoiceCallEvent;

/// <summary>A piece of the bot's voice: 16-bit little-endian samples, one channel, 24,000 a second.</summary>
public sealed record VoiceCallAudio(ReadOnlyMemory<byte> Samples) : VoiceCallEvent;

/// <summary>Something failed. The message is a sentence to show.</summary>
public sealed record VoiceCallFailed(string Message) : VoiceCallEvent;

/// <summary>The call is over. <see cref="Reason"/> is one of <see cref="VoiceCallEndReasons"/>.</summary>
public sealed record VoiceCallEnded(string Reason) : VoiceCallEvent;

/// <summary>
/// One live call with a bot over a WebSocket. One reader takes the events with <see cref="Read"/>; audio and
/// commands may be sent from anywhere, and are written one at a time. Disposing it hangs up.
/// </summary>
public sealed class VoiceCall(WebSocket socket) : IAsyncDisposable
{
    #region Private Fields

    /// <summary>The largest frame the server sends is a second or so of audio; this leaves room to spare.</summary>
    private const int MaxFrameBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim sending = new(1, 1);

    #endregion

    #region Public Methods

    /// <summary>Sends what the microphone heard: 16-bit little-endian samples, one channel, 24,000 a second.</summary>
    public Task SendAudio(ReadOnlyMemory<byte> samples, CancellationToken cancellationToken = default) =>
        Send(samples, WebSocketMessageType.Binary, cancellationToken);

    /// <summary>Tells the bot the person talked over it, and how many milliseconds of its answer they heard.</summary>
    public Task Interrupt(int heardMilliseconds, CancellationToken cancellationToken = default) =>
        Send(new VoiceCallFrame(VoiceCallMessages.Interrupt) { HeardMs = Math.Max(0, heardMilliseconds) }, cancellationToken);

    /// <summary>Asks the server to end the call. The events end with <see cref="VoiceCallEnded"/>.</summary>
    public Task HangUp(CancellationToken cancellationToken = default) =>
        Send(new VoiceCallFrame(VoiceCallMessages.Hangup), cancellationToken);

    /// <summary>
    /// The events of the call until it ends. A call that loses its connection ends with a
    /// <see cref="VoiceCallEnded"/> whose reason is <see cref="VoiceCallEndReasons.Server"/>, so a reader always
    /// learns that it is over.
    /// </summary>
    public async IAsyncEnumerable<VoiceCallEvent> Read([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new byte[16 * 1024];

        while (true)
        {
            var received = await Receive(buffer, cancellationToken);

            if (received.Over)
            {
                yield return new VoiceCallEnded(VoiceCallEndReasons.Server);
                yield break;
            }

            buffer = received.Buffer;

            if (ToEvent(received.Type, buffer.AsMemory(0, received.Count)) is not { } happened)
            {
                continue;
            }

            yield return happened;

            if (happened is VoiceCallEnded)
            {
                yield break;
            }
        }
    }

    #endregion

    #region Interface Implementations

    public async ValueTask DisposeAsync()
    {
        if (socket.State is WebSocketState.Open && await Close() is not null)
        {
            // The other side is already gone, which is what closing was for.
            socket.Abort();
        }

        socket.Dispose();
        sending.Dispose();
    }

    #endregion

    #region Private Methods

    /// <summary>Says goodbye within two seconds. What stopped it, if anything did.</summary>
    private async Task<Exception?> Close()
    {
        try
        {
            using var closing = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closing.Token);
            return null;
        }
        catch (Exception gone) when (gone is WebSocketException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            return gone;
        }
    }

    private Task Send(VoiceCallFrame frame, CancellationToken cancellationToken) =>
        Send(JsonSerializer.SerializeToUtf8Bytes(frame, json), WebSocketMessageType.Text, cancellationToken);

    private async Task Send(ReadOnlyMemory<byte> bytes, WebSocketMessageType type, CancellationToken cancellationToken)
    {
        await sending.WaitAsync(cancellationToken);

        try
        {
            if (socket.State is WebSocketState.Open)
            {
                await socket.SendAsync(bytes, type, endOfMessage: true, cancellationToken);
            }
        }
        finally
        {
            sending.Release();
        }
    }

    /// <summary>One whole message, or the end of the call. The buffer grows to fit, up to a bound.</summary>
    private async Task<Received> Receive(byte[] buffer, CancellationToken cancellationToken)
    {
        var filled = 0;

        try
        {
            while (true)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(filled), cancellationToken);
                filled += received.Count;

                if (received.MessageType is WebSocketMessageType.Close)
                {
                    return new Received(received.MessageType, buffer, 0, Over: true);
                }

                if (received.EndOfMessage)
                {
                    return new Received(received.MessageType, buffer, filled, Over: false);
                }

                if (filled == buffer.Length)
                {
                    if (buffer.Length >= MaxFrameBytes)
                    {
                        return new Received(received.MessageType, buffer, 0, Over: true);
                    }

                    Array.Resize(ref buffer, buffer.Length * 2);
                }
            }
        }
        catch (Exception lost) when (lost is WebSocketException or IOException or ObjectDisposedException)
        {
            return new Received(WebSocketMessageType.Close, buffer, 0, Over: true) { Lost = lost };
        }
    }

    private static VoiceCallEvent? ToEvent(WebSocketMessageType type, ReadOnlyMemory<byte> bytes)
    {
        if (type is WebSocketMessageType.Binary)
        {
            return new VoiceCallAudio(bytes.ToArray());
        }

        // A frame this client cannot read is skipped, as a type it does not know is.
        var frame = ReadFrame(bytes).Frame;

        return frame?.Type switch
        {
            VoiceCallMessages.Ready => new VoiceCallReady(frame.Model ?? string.Empty, frame.Voice ?? string.Empty, frame.MaxMinutes),
            VoiceCallMessages.PersonStarted => new VoiceCallPersonStarted(),
            VoiceCallMessages.PersonSaid => new VoiceCallPersonSaid(frame.Text ?? string.Empty),
            VoiceCallMessages.BotSaying => new VoiceCallBotSaying(frame.Text ?? string.Empty),
            VoiceCallMessages.BotSaid => new VoiceCallBotSaid(frame.Text ?? string.Empty),
            VoiceCallMessages.Failed => new VoiceCallFailed(frame.Message ?? string.Empty),
            VoiceCallMessages.Ended => new VoiceCallEnded(frame.Reason ?? VoiceCallEndReasons.Server),
            _ => null,
        };
    }

    private static (VoiceCallFrame? Frame, JsonException? Unreadable) ReadFrame(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            return (JsonSerializer.Deserialize<VoiceCallFrame>(bytes.Span, json), null);
        }
        catch (JsonException unreadable)
        {
            return (null, unreadable);
        }
    }

    #endregion

    #region Nested Types

    /// <summary>One whole message, or the end of the call: closed by the server, lost, or a frame too large to be one of its.</summary>
    private readonly record struct Received(WebSocketMessageType Type, byte[] Buffer, int Count, bool Over)
    {
        public Exception? Lost { get; init; }
    }

    #endregion
}
