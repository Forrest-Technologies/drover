namespace Drover.Adapters.OpenAICompatible;

/// <summary>
/// Speech over the compatible audio protocol: a transcription endpoint that takes a recording and a speech
/// endpoint that returns audio. Hosted providers and self-hosted speech servers both speak it.
/// </summary>
internal sealed class ProtocolSpeech(HttpClient client)
{
    #region Private Fields

    private const int MaxTranscriptBytes = 4 * 1024 * 1024;

    private const int MaxVoiceListBytes = 1024 * 1024;

    private const int MaxVoices = 2000;

    private const int MaxVoiceIdLength = 200;

    private const int ChunkBytes = 16 * 1024;

    #endregion

    #region Public Methods

    public async Task<Transcript> Transcribe(
        ModelEndpoint connection,
        string? apiKey,
        TranscriptionRequest transcription,
        CancellationToken cancellationToken)
    {
        // Held in memory, never on disk, and bounded: a request that has to be sent again can be.
        var audio = await ReadBounded(transcription.Audio, transcription.MaxBytes, cancellationToken);

        using var request = NewRequest(HttpMethod.Post, connection, "audio/transcriptions", apiKey);
        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = MediaTypeHeaderValue.TryParse(transcription.MediaType, out var mediaType)
            ? mediaType
            : new MediaTypeHeaderValue("application/octet-stream");
        var form = new MultipartFormDataContent
        {
            { file, "file", FileName(transcription.MediaType) },
            { new StringContent(transcription.Model), "model" },
            { new StringContent("json"), "response_format" },
        };

        if (transcription.Language is { Length: > 0 } language)
        {
            form.Add(new StringContent(language), "language");
        }

        request.Content = form;

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            EnsureSucceeded(response);
            using var document = await BoundedResponse.ReadJson(response.Content, MaxTranscriptBytes, "transcript", cancellationToken);
            return ReadTranscript(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> Speak(
        ModelEndpoint connection,
        string? apiKey,
        SpeechRequest speech,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await StartSpeech(connection, apiKey, speech, cancellationToken);
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);

        while (true)
        {
            var chunk = await ReadChunk(body, cancellationToken);

            if (chunk.Length == 0)
            {
                yield break;
            }

            yield return chunk;
        }
    }

    /// <summary>
    /// The voices the server lists at <c>audio/voices</c>. Self-hosted speech servers have it; a provider without
    /// it answers 404, and the voices Drover knows for the model are returned in its place.
    /// </summary>
    public async Task<IReadOnlyList<VoiceOption>> ListVoices(
        ModelEndpoint connection,
        string? apiKey,
        string model,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Get, connection, "audio/voices", apiKey);

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
            {
                return KnownVoices.For(connection.BaseAddress, model);
            }

            EnsureSucceeded(response);
            using var document = await BoundedResponse.ReadJson(response.Content, MaxVoiceListBytes, "voice list", cancellationToken);
            var listed = ReadVoices(document.RootElement);
            return listed.Count > 0 ? listed : KnownVoices.For(connection.BaseAddress, model);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            throw ModelProviderException.FromMalformedResponse();
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
    }

    #endregion

    #region Requests

    private async Task<HttpResponseMessage> StartSpeech(
        ModelEndpoint connection,
        string? apiKey,
        SpeechRequest speech,
        CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Post, connection, "audio/speech", apiKey);
        Dictionary<string, object> body = new()
        {
            ["model"] = speech.Model,
            ["input"] = speech.Text,
            ["voice"] = speech.Voice,
            ["response_format"] = speech.Format.WireName(),
        };

        if (Math.Abs(speech.Speed - 1) > 0.001)
        {
            body["speed"] = speech.Speed;
        }

        request.Content = JsonContent.Create(body);
        HttpResponseMessage? response = null;

        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            EnsureSucceeded(response);
            return response;
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            response?.Dispose();
            throw mapped;
        }
        catch
        {
            response?.Dispose();
            throw;
        }
    }

    private static async Task<ReadOnlyMemory<byte>> ReadChunk(Stream body, CancellationToken cancellationToken)
    {
        var buffer = new byte[ChunkBytes];

        try
        {
            var read = await body.ReadAsync(buffer, cancellationToken);
            return buffer.AsMemory(0, read);
        }
        catch (Exception exception) when (ProtocolFailureMapping.TryMap(exception, out var mapped))
        {
            throw mapped;
        }
        catch (IOException)
        {
            throw ModelProviderException.FromNetwork();
        }
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, ModelEndpoint connection, string path, string? apiKey)
    {
        var request = new HttpRequestMessage(method, new Uri(WithTrailingSlash(connection.BaseAddress), path));

        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    private static void EnsureSucceeded(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var retryAfter = ProtocolFailureMapping.ParseRetryAfter(
            response.Headers.TryGetValues("Retry-After", out var values) ? values.FirstOrDefault() : null,
            DateTimeOffset.UtcNow);
        throw ModelProviderException.FromStatus((int)response.StatusCode, retryAfter);
    }

    private static async Task<byte[]> ReadBounded(Stream audio, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[ChunkBytes];
        int read;

        while ((read = await audio.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                throw new ModelProviderException(ModelFailure.Rejected, "The recording is larger than this server accepts.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        if (buffer.Length == 0)
        {
            throw new ModelProviderException(ModelFailure.Rejected, "The recording is empty.");
        }

        return buffer.ToArray();
    }

    #endregion

    #region Response Reading

    private static Transcript ReadTranscript(JsonElement root)
    {
        if (!root.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
        {
            throw new FormatException("The transcript has no text.");
        }

        return new Transcript(text.GetString()!.Trim())
        {
            Seconds = ReadSeconds(root),
            Language = root.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String
                ? language.GetString()
                : null,
        };
    }

    private static double? ReadSeconds(JsonElement root)
    {
        if (root.TryGetProperty("duration", out var duration) && duration.ValueKind == JsonValueKind.Number
            && duration.TryGetDouble(out var seconds) && seconds is >= 0 and < 1_000_000)
        {
            return seconds;
        }

        return root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty("seconds", out var used) && used.ValueKind == JsonValueKind.Number
            && used.TryGetDouble(out var billed) && billed is >= 0 and < 1_000_000
                ? billed
                : null;
    }

    /// <summary>Servers answer with a list of names, or a list of objects that carry an id or a name.</summary>
    private static List<VoiceOption> ReadVoices(JsonElement root)
    {
        var list = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("voices", out var voices) && voices.ValueKind == JsonValueKind.Array
                ? voices
                : root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                    ? data
                    : default;

        if (list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. list.EnumerateArray()
                .Select(ReadVoice)
                .OfType<VoiceOption>()
                .DistinctBy(voice => voice.Id, StringComparer.Ordinal)
                .Take(MaxVoices),
        ];
    }

    private static VoiceOption? ReadVoice(JsonElement voice)
    {
        var id = voice.ValueKind switch
        {
            JsonValueKind.String => voice.GetString(),
            JsonValueKind.Object => ReadString(voice, "id") ?? ReadString(voice, "voice_id") ?? ReadString(voice, "name"),
            _ => null,
        };

        if (id is not { Length: > 0 and <= MaxVoiceIdLength } || id.Any(char.IsControl))
        {
            return null;
        }

        var name = voice.ValueKind == JsonValueKind.Object ? ReadString(voice, "name") : null;
        return new VoiceOption(id, name is { Length: > 0 and <= MaxVoiceIdLength } && !name.Any(char.IsControl) ? name : id);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    #endregion

    #region Private Methods

    private static string FileName(string mediaType) =>
        mediaType.Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "audio/wav" or "audio/x-wav" or "audio/wave" => "speech.wav",
            "audio/mpeg" or "audio/mp3" => "speech.mp3",
            "audio/mp4" or "audio/m4a" or "audio/x-m4a" or "audio/aac" => "speech.m4a",
            "audio/ogg" or "audio/opus" => "speech.ogg",
            "audio/flac" or "audio/x-flac" => "speech.flac",
            _ => "speech.webm",
        };

    private static Uri WithTrailingSlash(Uri address) =>
        address.AbsoluteUri.EndsWith('/') ? address : new Uri($"{address.AbsoluteUri}/");

    #endregion
}
