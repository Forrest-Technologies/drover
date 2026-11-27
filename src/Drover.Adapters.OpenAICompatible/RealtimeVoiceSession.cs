namespace Drover.Adapters.OpenAICompatible;

/// <summary>One open realtime call. Reads and writes are each serialized; a read and a write may overlap.</summary>
internal sealed class RealtimeVoiceSession(VoiceSocket socket, RealtimeDialect dialect) : IRealtimeVoiceSession
{
    #region Private Fields

    private const int MaxSpokenCharacters = 200_000;

    // Sent with the events of an interruption, so an error that answers one of them is known for what it is.
    private const string InterruptEvent = "drover_interrupt";

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
            : socket.Send(new { type = "input_audio_buffer.append", audio = Convert.ToBase64String(samples.Span) }, cancellationToken);

    /// <summary>
    /// The provider cuts its own answer off when it hears the person, so the answer is cancelled here only while it
    /// is still being made. What it remembers is then cut to what was heard, never past the audio it sent: a cut
    /// beyond that is refused.
    /// </summary>
    public async ValueTask Interrupt(int heardMilliseconds, CancellationToken cancellationToken)
    {
        if (responding)
        {
            await socket.Send(new { type = "response.cancel", event_id = InterruptEvent }, cancellationToken);
        }

        if (TakeSpeaking() is (var item, var sentMilliseconds))
        {
            var heard = Math.Clamp(heardMilliseconds, 0, sentMilliseconds);
            await socket.Send(
                new { type = "conversation.item.truncate", event_id = InterruptEvent, item_id = item, content_index = 0, audio_end_ms = heard },
                cancellationToken);
        }
    }

    /// <summary>A model of this protocol answers by itself and hands nothing over, so there is never anything to report.</summary>
    public ValueTask Report(string workId, string text, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public async IAsyncEnumerable<RealtimeVoiceEvent> Read([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await socket.Receive(cancellationToken) is { } message)
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
        socket.Dispose();
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

        await socket.Send(new { type = "session.update", session }, cancellationToken);

        foreach (var turn in options.History)
        {
            await socket.Send(
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
        switch (VoiceSocket.ReadString(root, "type"))
        {
            case "response.created":
                responding = true;
                break;

            case "response.output_audio.delta" or "response.audio.delta":
                if (VoiceSocket.ReadString(root, "delta") is { Length: > 0 } audio && VoiceSocket.TryDecode(audio, out var samples))
                {
                    Speaking(VoiceSocket.ReadString(root, "item_id"), samples.Length);
                    yield return new RealtimeAudioOut(samples);
                }

                break;

            case "response.output_audio_transcript.delta" or "response.audio_transcript.delta":
                if (VoiceSocket.ReadString(root, "delta") is { Length: > 0 } words)
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
                if (VoiceSocket.ReadString(root, "transcript")?.Trim() is { Length: > 0 } heard)
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
                yield return VoiceSocket.DescribeError(root);
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
        && (VoiceSocket.ReadString(error, "event_id") == InterruptEvent || VoiceSocket.ReadString(error, "code") == "response_cancel_not_active");

    private static RealtimeTurnUsage? ReadUsage(JsonElement root) =>
        root.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Object
        && response.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            ? new RealtimeTurnUsage(VoiceSocket.ReadCount(usage, "input_tokens"), VoiceSocket.ReadCount(usage, "output_tokens"))
            : null;

    #endregion
}
