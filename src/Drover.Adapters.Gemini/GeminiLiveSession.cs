namespace Drover.Adapters.Gemini;

/// <summary>
/// A Gemini Live call. Google resamples input declared as 24 kHz and sends output in that format.
/// Server activity detection owns interruption and removes unplayed audio from the provider's context.
/// </summary>
internal sealed class GeminiLiveSession(ClientWebSocket socket) : IRealtimeVoiceSession
{
    #region Private Fields

    private const int MaxEventBytes = 4 * 1024 * 1024;

    private static readonly string[] audioModalities = ["AUDIO"];

    private static readonly TimeSpan setupLimit = TimeSpan.FromSeconds(15);

    private readonly GeminiLiveEvents events = new();

    #endregion

    #region Public Methods

    public async Task Start(HttpMessageInvoker client, Uri address, RealtimeVoiceOptions options, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(setupLimit);

        try
        {
            await socket.ConnectAsync(address, client, deadline.Token);
            await Configure(options, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw ModelProviderException.FromTimeout();
        }
        catch (WebSocketException failure)
        {
            if (GeminiFailures.TryMap(failure, out var mapped) && mapped.Failure is ModelFailure.Rejected)
            {
                throw mapped;
            }

            throw socket.HttpStatusCode is >= HttpStatusCode.BadRequest
                ? ModelProviderException.FromStatus((int)socket.HttpStatusCode)
                : ModelProviderException.FromNetwork();
        }
    }

    #endregion

    #region Interface Implementations

    public ValueTask SendAudio(ReadOnlyMemory<byte> samples, CancellationToken cancellationToken) =>
        Send(new
        {
            realtimeInput = new
            {
                audio = new { data = samples, mimeType = $"{RealtimeAudio.MediaType};rate={RealtimeAudio.SampleRate}" },
            },
        }, cancellationToken);

    /// <summary>Gemini cuts the turn on speech activity and offers no separate playback-offset command.</summary>
    public ValueTask Interrupt(int heardMilliseconds, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask Report(string workId, string text, CancellationToken cancellationToken) =>
        ValueTask.FromException(new InvalidOperationException("This call answers directly."));

    public async IAsyncEnumerable<RealtimeVoiceEvent> Read([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            using var message = await Receive(cancellationToken);

            if (message is null)
            {
                foreach (var remaining in events.Finish())
                {
                    yield return remaining;
                }

                yield break;
            }

            ThrowIfRefused(message.RootElement);

            foreach (var happened in events.Read(message.RootElement))
            {
                yield return happened;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        socket.Abort();
        socket.Dispose();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Private Methods

    private async Task Configure(RealtimeVoiceOptions options, CancellationToken cancellation)
    {
        await Send(new
        {
            setup = new
            {
                model = $"models/{options.Model}",
                generationConfig = new
                {
                    responseModalities = audioModalities,
                    speechConfig = new
                    {
                        voiceConfig = new { prebuiltVoiceConfig = new { voiceName = options.Voice } },
                    },
                },
                systemInstruction = new { parts = new[] { new { text = options.Instructions } } },
                inputAudioTranscription = new { },
                outputAudioTranscription = new { },
                historyConfig = new { initialHistoryInClientContent = options.History.Count > 0 },
            },
        }, cancellation);

        using var ready = await Receive(cancellation)
            ?? throw new ModelProviderException(ModelFailure.Rejected, "The provider refused to start the live call.");

        ThrowIfRefused(ready.RootElement);
        if (!ready.RootElement.TryGetProperty("setupComplete", out var setup) || setup.ValueKind is not JsonValueKind.Object)
        {
            throw ModelProviderException.FromMalformedResponse();
        }

        if (options.History.Count > 0)
        {
            await Send(new
            {
                clientContent = new
                {
                    turns = options.History.Select(turn => new
                    {
                        role = turn.FromBot ? "model" : "user",
                        parts = new[] { new { text = turn.Text } },
                    }),
                    turnComplete = true,
                },
            }, cancellation);
        }
    }

    private async ValueTask Send(object message, CancellationToken cancellation)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message);

        try
        {
            await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, cancellation);
        }
        catch (Exception failure) when (failure is WebSocketException or IOException or ObjectDisposedException)
        {
            throw ModelProviderException.FromNetwork();
        }
    }

    private async Task<JsonDocument?> Receive(CancellationToken cancellation)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        using var message = new MemoryStream();

        try
        {
            ValueWebSocketReceiveResult part;
            do
            {
                part = await socket.ReceiveAsync(buffer.AsMemory(), cancellation);

                if (part.MessageType is WebSocketMessageType.Close)
                {
                    return socket.CloseStatus is null or WebSocketCloseStatus.NormalClosure or WebSocketCloseStatus.EndpointUnavailable
                        ? null
                        : throw new ModelProviderException(ModelFailure.Rejected, "The provider ended the live call.");
                }

                if (message.Length + part.Count > MaxEventBytes)
                {
                    throw ModelProviderException.FromLimit("call event");
                }

                await message.WriteAsync(buffer.AsMemory(0, part.Count), cancellation);
            } while (!part.EndOfMessage);

            return Parse(message);
        }
        catch (JsonException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception failure) when (failure is WebSocketException or IOException or ObjectDisposedException)
        {
            throw ModelProviderException.FromNetwork();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static JsonDocument Parse(MemoryStream message)
    {
        var document = JsonDocument.Parse(message.ToArray());
        if (document.RootElement.ValueKind is JsonValueKind.Object)
        {
            return document;
        }

        document.Dispose();
        throw ModelProviderException.FromMalformedResponse();
    }

    private static void ThrowIfRefused(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) && error.ValueKind is JsonValueKind.Object)
        {
            throw GeminiFailures.FromStreamError(error);
        }
    }

    #endregion
}
