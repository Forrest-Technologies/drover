namespace Drover.Adapters.OpenAICompatible;

/// <summary>One open realtime call. Reads and writes are each serialized; a read and a write may overlap.</summary>
internal sealed class RealtimeVoiceSession(ClientWebSocket socket, RealtimeDialect dialect) : IRealtimeVoiceSession
{
    #region Private Fields

    private const int MaxEventBytes = 4 * 1024 * 1024;

    private const int MaxSpokenCharacters = 200_000;

    // Sent with the events of an interruption, so an error that answers one of them is known for what it is.
    private const string InterruptEvent = "drover_interrupt";

    private readonly SemaphoreSlim sending = new(1, 1);

    private readonly StringBuilder spoken = new();

    private readonly Lock answer = new();

    // The item whose audio is arriving, and how many bytes of it have arrived.
    private string? speakingItem;

    private long speakingBytes;

    // The item last cut. Audio of it that was already on its way is not an answer being spoken.
    private string? cutItem;

    // True from the provider starting an answer until it says the answer is done, finished or cut off.
    private volatile bool responding;

    #endregion

    #region Interface Implementations

    public ValueTask SendAudio(ReadOnlyMemory<byte> samples, CancellationToken cancellationToken) =>
        samples.IsEmpty
            ? ValueTask.CompletedTask
            : Send(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(samples.Span) }, cancellationToken);

    /// <summary>
    /// The provider cuts its own answer off when it hears the person, so the answer is cancelled here only while it
    /// is still being made. What it remembers is then cut to what was heard, never past the audio it sent: a cut
    /// beyond that is refused.
    /// </summary>
    public async ValueTask Interrupt(int heardMilliseconds, CancellationToken cancellationToken)
    {
        if (responding)
        {
            await Send(new { type = "response.cancel", event_id = InterruptEvent }, cancellationToken);
        }

        if (TakeSpeaking() is (var item, var sentMilliseconds))
        {
            var heard = Math.Clamp(heardMilliseconds, 0, sentMilliseconds);
            await Send(
                new { type = "conversation.item.truncate", event_id = InterruptEvent, item_id = item, content_index = 0, audio_end_ms = heard },
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
        Dictionary<string, object?> turns = new()
        {
            ["type"] = dialect.ListensForMeaning ? "semantic_vad" : "server_vad",
            ["create_response"] = true,
            ["interrupt_response"] = true,
        };
        Dictionary<string, object?> input = new() { ["format"] = format };

        if (dialect.ListensForMeaning)
        {
            // A call is held on a loudspeaker unless a headset is worn, so the microphone is treated as a distant one.
            input["noise_reduction"] = new { type = "far_field" };
        }

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
            case "response.created":
                responding = true;
                break;

            case "response.output_audio.delta" or "response.audio.delta":
                if (ReadString(root, "delta") is { Length: > 0 } audio && TryDecode(audio, out var samples))
                {
                    Speaking(ReadString(root, "item_id"), samples.Length);
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
                if (ReadString(root, "transcript")?.Trim() is { Length: > 0 } heard)
                {
                    yield return new RealtimePersonSaid(heard);
                }

                break;

            case "response.done":
                responding = false;
                var said = spoken.ToString().Trim();
                spoken.Clear();

                if (said.Length > 0)
                {
                    yield return new RealtimeBotSaid(said);
                }

                if (ReadUsage(root) is { } usage)
                {
                    yield return usage;
                }

                break;

            case "error" when !AnswersAnInterruption(root):
                yield return DescribeError(root);
                break;
        }
    }

    private void Speaking(string? item, int bytes)
    {
        lock (answer)
        {
            if (item is null || item == cutItem)
            {
                return;
            }

            if (item != speakingItem)
            {
                (speakingItem, speakingBytes) = (item, 0);
            }

            speakingBytes += bytes;
        }
    }

    /// <summary>The item being spoken and how many milliseconds of it were sent, once: an item is cut a single time.</summary>
    private (string Item, int SentMilliseconds)? TakeSpeaking()
    {
        lock (answer)
        {
            if (speakingItem is not { } item)
            {
                return null;
            }

            var sent = (int)(speakingBytes / RealtimeAudio.BytesPerSample * 1000 / RealtimeAudio.SampleRate);
            (speakingItem, speakingBytes, cutItem) = (null, 0, item);
            return (item, sent);
        }
    }

    /// <summary>
    /// An interruption races the provider's own: the answer may already be over when the cancel arrives. The
    /// provider says so in an error and the call goes on unharmed, so that error is not a failure to show.
    /// </summary>
    private static bool AnswersAnInterruption(JsonElement root) =>
        root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
        && (ReadString(error, "event_id") == InterruptEvent || ReadString(error, "code") == "response_cancel_not_active");

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
