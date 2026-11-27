namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// A live call over the realtime protocol: one WebSocket that carries the person's audio up and the model's audio
/// and words down. The connection is made through the factory's client, so the deployment's outbound checks and
/// the space's private-address rule apply to it as to any other provider request.
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
        var socket = new ClientWebSocket();

        foreach (var (name, value) in ProtocolKey.Headers(connection.BaseAddress, apiKey))
        {
            socket.Options.SetRequestHeader(name, value);
        }

        try
        {
            await socket.ConnectAsync(Address(connection.BaseAddress, options.Model), client, cancellationToken);
            var session = new RealtimeVoiceSession(socket, RealtimeDialect.For(connection.BaseAddress));
            await session.Configure(options, cancellationToken);
            return session;
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            socket.Dispose();
            throw Describe(exception);
        }
    }

    #endregion

    #region Private Methods

    private static Uri Address(Uri baseAddress, string model)
    {
        var builder = new UriBuilder(baseAddress)
        {
            Scheme = baseAddress.Scheme == Uri.UriSchemeHttp ? "ws" : "wss",
            Query = $"model={Uri.EscapeDataString(model)}",
        };
        builder.Path = $"{builder.Path.TrimEnd('/')}/realtime";
        return builder.Uri;
    }

    /// <summary>The handshake's failure in Drover's words. The provider's own text is never kept.</summary>
    private static ModelProviderException Describe(Exception exception)
    {
        if (ProtocolFailureMapping.FindRefusal(exception) is { } refusal)
        {
            return refusal;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException { StatusCode: { } status })
            {
                return ModelProviderException.FromStatus((int)status);
            }
        }

        return exception is WebSocketException { WebSocketErrorCode: WebSocketError.NotAWebSocket }
            ? new ModelProviderException(
                ModelFailure.Rejected,
                "The provider refused the call. Check the key on the connection and that the model takes live calls.")
            : ModelProviderException.FromNetwork();
    }

    #endregion
}
