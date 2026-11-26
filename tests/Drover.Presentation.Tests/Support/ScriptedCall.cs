namespace Drover.Presentation.Tests.Support;

/// <summary>
/// The server's side of a live call, as a socket a test writes to and reads from: the frames the server would send
/// are queued for the client, and everything the client sent is kept. A hang-up is answered with the server's own
/// goodbye, as the real endpoint does.
/// </summary>
public sealed class ScriptedCall : WebSocket
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private readonly Channel<(WebSocketMessageType Type, byte[] Bytes)> incoming = Channel.CreateUnbounded<(WebSocketMessageType, byte[])>();

    private WebSocketState state = WebSocketState.Open;

    #endregion

    #region Properties

    /// <summary>The audio the client sent, piece by piece.</summary>
    public List<byte[]> AudioSent { get; } = [];

    /// <summary>The text frames the client sent, read as frames.</summary>
    public List<VoiceCallFrame> FramesSent { get; } = [];

    public override WebSocketCloseStatus? CloseStatus => null;

    public override string? CloseStatusDescription => null;

    public override WebSocketState State => state;

    public override string? SubProtocol => null;

    #endregion

    #region Public Methods

    /// <summary>The server sends a text frame.</summary>
    public void Say(VoiceCallFrame frame) =>
        incoming.Writer.TryWrite((WebSocketMessageType.Text, JsonSerializer.SerializeToUtf8Bytes(frame, json)));

    /// <summary>The server sends a piece of the bot's voice.</summary>
    public void Play(params byte[] samples) => incoming.Writer.TryWrite((WebSocketMessageType.Binary, samples));

    /// <summary>The connection drops without a goodbye.</summary>
    public void Drop() => incoming.Writer.TryWrite((WebSocketMessageType.Close, []));

    #endregion

    #region Interface Implementations

    public override void Abort() => state = WebSocketState.Aborted;

    public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) =>
        CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

    public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
    {
        state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override void Dispose() => state = WebSocketState.Closed;

    public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
    {
        var received = await ReceiveAsync(buffer.AsMemory(), cancellationToken);
        return new(received.Count, received.MessageType, received.EndOfMessage);
    }

    public override async ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var (type, bytes) = await incoming.Reader.ReadAsync(cancellationToken);
        bytes.CopyTo(buffer);
        return new(bytes.Length, type, endOfMessage: true);
    }

    public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
        SendAsync(buffer.AsMemory(), messageType, endOfMessage, cancellationToken).AsTask();

    public override ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
    {
        if (messageType is WebSocketMessageType.Binary)
        {
            AudioSent.Add(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        var frame = JsonSerializer.Deserialize<VoiceCallFrame>(buffer.Span, json)!;
        FramesSent.Add(frame);

        if (frame.Type is VoiceCallMessages.Hangup)
        {
            Say(new(VoiceCallMessages.Ended) { Reason = VoiceCallEndReasons.HungUp });
        }

        return ValueTask.CompletedTask;
    }

    #endregion
}
