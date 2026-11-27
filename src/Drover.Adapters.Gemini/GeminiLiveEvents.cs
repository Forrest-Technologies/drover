namespace Drover.Adapters.Gemini;

/// <summary>Turns provider messages into call events and keeps transcript fragments until they form a turn.</summary>
internal sealed class GeminiLiveEvents
{
    #region Private Fields

    private const int MaxTranscriptCharacters = 32 * 1024;

    private readonly StringBuilder person = new();

    private readonly StringBuilder bot = new();

    private bool personStarted;

    private bool botStarted;

    #endregion

    #region Public Methods

    public IEnumerable<RealtimeVoiceEvent> Read(JsonElement message)
    {
        if (Object(message, "voiceActivity") is { } activity && Text(activity, "type") == "ACTIVITY_START")
        {
            foreach (var started in StartPerson())
            {
                yield return started;
            }
        }

        if (Object(message, "serverContent") is { } content)
        {
            foreach (var happened in ReadContent(content))
            {
                yield return happened;
            }
        }

        if (Object(message, "usageMetadata") is { } usage)
        {
            yield return new RealtimeTurnUsage(Count(usage, "promptTokenCount"), Count(usage, "responseTokenCount"));
        }
    }

    public IEnumerable<RealtimeVoiceEvent> Finish()
    {
        if (Take(person) is { Length: > 0 } spoken)
        {
            yield return new RealtimePersonSaid(spoken);
        }

        if (botStarted || bot.Length > 0)
        {
            botStarted = false;
            yield return new RealtimeBotSaid(Take(bot));
        }
    }

    #endregion

    #region Private Methods

    private IEnumerable<RealtimeVoiceEvent> StartPerson()
    {
        if (!personStarted)
        {
            personStarted = true;
            yield return new RealtimePersonStartedSpeaking();
        }
    }

    private IEnumerable<RealtimeVoiceEvent> ReadContent(JsonElement content)
    {
        if (Flag(content, "interrupted"))
        {
            foreach (var started in StartPerson())
            {
                yield return started;
            }

            botStarted = false;
            yield return new RealtimeBotSaid(Take(bot));
        }

        if (Object(content, "inputTranscription") is { } input && Text(input, "text") is { } heard)
        {
            Add(person, heard);
            if (Flag(input, "finished") && Take(person) is { Length: > 0 } spoken)
            {
                yield return new RealtimePersonSaid(spoken);
            }
        }

        if (!Flag(content, "interrupted"))
        {
            foreach (var spoken in ReadOutput(content))
            {
                yield return spoken;
            }
        }

        if (Flag(content, "turnComplete"))
        {
            foreach (var finished in Finish())
            {
                yield return finished;
            }

            personStarted = false;
        }
    }

    private IEnumerable<RealtimeVoiceEvent> ReadOutput(JsonElement content)
    {
        if (Object(content, "outputTranscription") is { } output && Text(output, "text") is { Length: > 0 } text)
        {
            Add(bot, text);
            botStarted = true;
            yield return new RealtimeBotSaying(text);
        }

        if (Object(content, "modelTurn") is { } turn && turn.TryGetProperty("parts", out var parts)
            && parts.ValueKind is JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.ValueKind is JsonValueKind.Object && Object(part, "inlineData") is { } audio
                    && Text(audio, "mimeType") is RealtimeAudio.MediaType or "audio/pcm;rate=24000"
                    && Text(audio, "data") is { Length: > 0 } data)
                {
                    var samples = new byte[data.Length / 4 * 3 + 3];
                    if (!Convert.TryFromBase64String(data, samples, out var written)
                        || written % RealtimeAudio.BytesPerSample != 0)
                    {
                        throw ModelProviderException.FromMalformedResponse();
                    }

                    botStarted = true;
                    yield return new RealtimeAudioOut(samples.AsMemory(0, written));
                }
            }
        }
    }

    private static void Add(StringBuilder buffer, string text)
    {
        if (text.Length > MaxTranscriptCharacters - buffer.Length)
        {
            throw ModelProviderException.FromLimit("call transcript");
        }

        buffer.Append(text);
    }

    private static string Take(StringBuilder buffer)
    {
        var text = buffer.ToString();
        buffer.Clear();
        return text;
    }

    private static JsonElement? Object(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Object ? value : null;

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;

    private static long Count(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.Number
            && value.TryGetInt64(out var count) && count >= 0 ? count : 0;

    private static bool Flag(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True;

    #endregion
}
