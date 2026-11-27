namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// A live call over the live protocol: one WebSocket to <c>{base}/live/sessions</c> that carries the person's audio
/// up and the model's audio and both speakers' words down. The model is named in the first message, never in the
/// address.
/// </summary>
internal static class ProtocolLiveVoice
{
    #region Private Fields

    /// <summary>How long the provider has to say the session started before the call is given up.</summary>
    private static readonly TimeSpan startWait = TimeSpan.FromSeconds(20);

    #endregion

    #region Public Methods

    public static async Task<IRealtimeVoiceSession> Open(
        HttpMessageInvoker client,
        ModelEndpoint connection,
        string? apiKey,
        RealtimeVoiceOptions options,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var socket = await VoiceSocket.Connect(client, VoiceSocket.Address(connection.BaseAddress, "live/sessions").Uri, apiKey, cancellationToken);
        using var starting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        starting.CancelAfter(startWait);

        try
        {
            var session = new LiveVoiceSession(socket, time);
            await session.Start(options, starting.Token);
            return session;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            socket.Dispose();
            throw ModelProviderException.FromNetwork();
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    #endregion
}
