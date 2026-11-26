namespace Drover.Adapters.OpenAICompatible;

/// <summary>One open realtime call. Reads and writes are each serialized; a read and a write may overlap.</summary>
internal sealed class RealtimeVoiceSession(ClientWebSocket socket, RealtimeDialect dialect) : IRealtimeVoiceSession
{
    #region Private Fields

    private const int MaxEventBytes = 4 * 1024 * 1024;

    private const int MaxSpokenCharacters = 200_000;

    private readonly SemaphoreSlim sending = new(1, 1);

    private readonly StringBuilder spoken = new();

    private string? speakingItem;

    #endregion

    #region Interface Implementations

    public ValueTask SendAudio(ReadOnlyMemory<byte> samples, CancellationToken cancellationToken) =>
        samples.IsEmpty
            ? ValueTask.CompletedTask
            : Send(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(samples.Span) }, cancellationToken);

    public async ValueTask Interrupt(int heardMilliseconds, CancellationToken cancellationToken)
    {
        await Send(new { type = "response.cancel" }, cancellationToken);

        if (Volatile.Read(ref speakingItem) is { } item)
        {
            await Send(
                new { type = "conversation.item.truncate", item_id = item, content_index = 0, audio_end_ms = Math.Max(0, heardMilliseconds) },
                cancellationToken);
        }
    }

    public async IAsyncEnumerable<RealtimeVoiceEvent> Read([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await Receive(cancellationToken) is { } message)
        {
            using (message)
            {
                foreach (var read in Translate(message.RootElement))
                {
                    yield return read;
                }
            }
        }
    }

    /// <summary>Hangs up. A call has nothing to flush, so the connection is dropped without a closing exchange.</summary>
    public ValueTask DisposeAsync()
    {
        socket.Abort();
        socket.Dispose();
        sending.Dispose();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Session Setup

    /// <summary>Tells the provider the voice, the bot's instructions and the audio format, then replays the earlier conversation.</summary>
    public async Task Configure(RealtimeVoiceOptions options, CancellationToken cancellationToken)
    {
        var format = new { type = RealtimeAudio.MediaType, rate = RealtimeAudio.SampleRate };
        var turns = new { type = "server_vad", create_response = true, interrupt_response = true };
        Dictionary<string, object?> input = new() { ["format"] = format };

        if (options.TranscriptionModel is { Length: > 0 } transcriber)
        {
            input["transcription"] = new { model = transcriber };
        }

        Dictionary<string, object?> output = new() { ["format"] = format };
        Dictionary<string, object?> session = new()
        {
            ["instructions"] = options.Instructions,
            ["audio"] = new Dictionary<string, object?> { ["input"] = input, ["output"] = output },
        };

        if (dialect.FlatSession)
        {
            session["voice"] = options.Voice;
            session["turn_detection"] = turns;
        }
        else
        {
            session["type"] = "realtime";
            session["output_modalities"] = new[] { "audio" };
            input["turn_detection"] = turns;
            output["voice"] = options.Voice;
        }

        await Send(new { type = "session.update", session }, cancellationToken);

        foreach (var turn in options.History)
        {
            await Send(
                new
                {
                    type = "conversation.item.create",
                    item = new
                    {
                        type = "message",
                        role = turn.FromBot ? "assistant" : "user",
                        content = new[] { new { type = turn.FromBot ? "output_text" : "input_text", text = turn.Text } },
                    },
                },
                cancellationToken);
        }
    }

    #endregion

    #region Reading

    /// <summary>
    /// One provider event as Drover's events. Both generations of event names are read, because servers that speak
    /// this protocol do not all use the same one.
    /// </summary>
    private IEnumerable<RealtimeVoiceEvent> Translate(JsonElement root)
    {
        switch (ReadString(root, "type"))
        {
            case "response.output_audio.delta" or "response.audio.delta":
                if (ReadString(root, "item_id") is { } item)
                {
                    Volatile.Write(ref speakingItem, item);
                }

                if (ReadString(root, "delta") is { Length: > 0 } audio && TryDecode(audio, out var samples))
                {
                    yield return new RealtimeAudioOut(samples);
                }

                break;

            case "response.output_audio_transcript.delta" or "response.audio_transcript.delta":
                if (ReadString(root, "delta") is { Length: > 0 } words)
                {
                    if (spoken.Length + words.Length <= MaxSpokenCharacters)
                    {
                        spoken.Append(words);
                    }

                    yield return new RealtimeBotSaying(words);
                }

                break;

            case "input_audio_buffer.speech_started":
                yield return new RealtimePersonStartedSpeaking();
                break;

            case "conversation.item.input_audio_transcription.completed":
                if (ReadString(root, "transcript")?.Trim() is { Length: > 0 } said)
                {
                    yield return new RealtimePersonSaid(said);
                }

                break;

            case "response.done":
                var answer = spoken.ToString().Trim();
                spoken.Clear();

                if (answer.Length > 0)
                {
                    yield return new RealtimeBotSaid(answer);
                }

                if (ReadUsage(root) is { } usage)
                {
                    yield return usage;
                }

                break;

            case "error":
                yield return DescribeError(root);
                break;
        }
    }

    private static RealtimeTurnUsage? ReadUsage(JsonElement root) =>
        root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object
        && response.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            ? new RealtimeTurnUsage(ReadCount(usage, "input_tokens"), ReadCount(usage, "output_tokens"))
            : null;

    /// <summary>The provider's error code decides the sentence. Its message is the provider's text and is dropped.</summary>
    private static RealtimeFailed DescribeError(JsonElement root)
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

    /// <summary>The next whole text message, or null when the call has ended. A message past the size bound ends it.</summary>
    private async Task<JsonDocument?> Receive(CancellationToken cancellationToken)
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

    #endregion

    #region Private Methods

    private async ValueTask Send(object message, CancellationToken cancellationToken)
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

    private static bool TryDecode(string encoded, out ReadOnlyMemory<byte> samples)
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

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long ReadCount(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var count) && count >= 0 ? count : 0;

    #endregion
}
