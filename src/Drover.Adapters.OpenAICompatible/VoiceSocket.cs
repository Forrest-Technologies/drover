namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// The connection of a live call: one WebSocket that carries JSON text messages both ways. Writes are serialized, and
/// a read and a write may overlap. It is opened through the factory's client, so the deployment's outbound checks and
/// the space's private-address rule apply to it as to any other provider request.
/// </summary>
internal sealed class VoiceSocket(ClientWebSocket socket) : IDisposable
{
    #region Private Fields

    private const int MaxEventBytes = 4 * 1024 * 1024;

    private readonly SemaphoreSlim sending = new(1, 1);

    #endregion

    #region Public Methods

    public static async Task<VoiceSocket> Connect(HttpMessageInvoker client, Uri address, string? apiKey, CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();

        foreach (var (name, value) in ProtocolKey.Headers(address, apiKey))
        {
            socket.Options.SetRequestHeader(name, value);
        }

        try
        {
            await socket.ConnectAsync(address, client, cancellationToken);
            return new VoiceSocket(socket);
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            socket.Dispose();
            throw DescribeHandshake(exception);
        }
    }

    /// <summary>The address of a call: the connection's own, as a WebSocket, with <paramref name="path"/> after it.</summary>
    public static UriBuilder Address(Uri baseAddress, string path)
    {
        var builder = new UriBuilder(baseAddress) { Scheme = baseAddress.Scheme == Uri.UriSchemeHttp ? "ws" : "wss" };
        builder.Path = $"{builder.Path.TrimEnd('/')}/{path}";
        return builder;
    }

    public async ValueTask Send(object message, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
        await sending.WaitAsync(cancellationToken);

        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }
        catch (Exception exception) when (exception is WebSocketException or IOException or ObjectDisposedException)
        {
            throw ModelProviderException.FromNetwork();
        }
        finally
        {
            sending.Release();
        }
    }


    /// <summary>The next whole text message, or null when the call has ended. A message past the size bound ends it.</summary>
    public async Task<JsonDocument?> Receive(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        using var message = new MemoryStream();

        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                if (message.Length + result.Count > MaxEventBytes)
                {
                    throw ModelProviderException.FromLimit("call event");
                }

                await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);

                if (!result.EndOfMessage)
                {
                    continue;
                }

                if (result.MessageType == WebSocketMessageType.Text && TryParse(message, out var document))
                {
                    return document;
                }

                message.SetLength(0);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or IOException or ObjectDisposedException)
        {
            if (socket.State is WebSocketState.Closed or WebSocketState.CloseReceived or WebSocketState.CloseSent)
            {
                return null;
            }

            throw ModelProviderException.FromNetwork();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>The provider's error code decides the sentence. Its message is the provider's text and is dropped.</summary>
    public static RealtimeFailed DescribeError(JsonElement root)
    {
        var code = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            ? ReadString(error, "code") ?? ReadString(error, "type")
            : null;

        return code switch
        {
            "invalid_api_key" or "authentication_error" => new RealtimeFailed(
                ModelFailure.InvalidKey,
                "The provider rejected the API key. Check the key on the connection and save it again."),
            "rate_limit_exceeded" or "rate_limit_error" => new RealtimeFailed(
                ModelFailure.RetryLater,
                "The provider is limiting requests right now. Try again in a moment."),
            "insufficient_quota" => new RealtimeFailed(ModelFailure.OutOfCredit, "The provider says the account is out of credit."),
            "server_error" => new RealtimeFailed(ModelFailure.Unreachable, "The provider failed on its side. Try again shortly."),
            _ => new RealtimeFailed(ModelFailure.Rejected, "The provider refused part of the call."),
        };
    }

    public static bool TryDecode(string encoded, out ReadOnlyMemory<byte> samples)
    {
        var buffer = new byte[(encoded.Length * 3 / 4) + 3];

        if (Convert.TryFromBase64String(encoded, buffer, out var written))
        {
            samples = buffer.AsMemory(0, written);
            return true;
        }

        samples = default;
        return false;
    }

    public static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public static long ReadCount(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var count) && count >= 0 ? count : 0;

    #endregion

    #region Interface Implementations

    /// <summary>Hangs up. A call has nothing to flush, so the connection is dropped without a closing exchange.</summary>
    public void Dispose()
    {
        socket.Abort();
        socket.Dispose();
        sending.Dispose();
    }

    #endregion

    #region Private Methods

    private static bool TryParse(MemoryStream message, out JsonDocument document)
    {
        var reader = new Utf8JsonReader(message.GetBuffer().AsSpan(0, (int)message.Length));

        if (JsonDocument.TryParseValue(ref reader, out var parsed) && parsed.RootElement.ValueKind == JsonValueKind.Object)
        {
            document = parsed;
            return true;
        }

        parsed?.Dispose();
        document = null!;
        return false;
    }

    /// <summary>The handshake's failure in Drover's words. The provider's own text is never kept.</summary>
    private static ModelProviderException DescribeHandshake(Exception exception)
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
