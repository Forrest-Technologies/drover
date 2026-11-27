namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// A live call over the realtime protocol: one WebSocket that carries the person's audio up and the model's audio
/// and words down.
/// </summary>
internal static class ProtocolRealtimeVoice
{
    #region Public Methods

    public static async Task<IRealtimeVoiceSession> Open(
        HttpMessageInvoker client,
        ModelEndpoint connection,
        string? apiKey,
        RealtimeVoiceOptions options,
        CancellationToken cancellationToken)
    {
        var address = VoiceSocket.Address(connection.BaseAddress, "realtime");
        address.Query = $"model={Uri.EscapeDataString(options.Model)}";
        var socket = await VoiceSocket.Connect(client, address.Uri, apiKey, cancellationToken);

        try
        {
            var session = new RealtimeVoiceSession(socket, RealtimeDialect.For(connection.BaseAddress));
            await session.Configure(options, cancellationToken);
            return session;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    #endregion
}
