namespace Drover.Adapters.Tests.Voice;

public sealed class CompatibleSpeechConformanceTests : SpeechConformance
{
    #region Private Fields

    private readonly List<(string Path, string ContentType, string Body, string? Authorization)> sent = [];

    #endregion

    #region Adapter Hooks

    protected override SpeechUnderTest Start(string transcript, byte[] audio, IReadOnlyList<string> voices) =>
        Subject(request => request.RequestUri!.AbsolutePath switch
        {
            var path when path.EndsWith("/audio/transcriptions", StringComparison.Ordinal) =>
                ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { text = transcript, usage = new { type = "duration", seconds = 2.5 } })),
            var path when path.EndsWith("/audio/speech", StringComparison.Ordinal) =>
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableStream(new MemoryStream(audio))) },
            var path when path.EndsWith("/audio/voices", StringComparison.Ordinal) && voices.Count > 0 =>
                ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { voices = voices.Select(id => new { id, name = id }) })),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });

    protected override SpeechUnderTest StartFailing(int status, string providerText) =>
        Subject(_ => ScriptedHandler.Json((HttpStatusCode)status, JsonSerializer.Serialize(new { error = new { message = providerText } })));

    #endregion

    #region Wire

    [Fact]
    public async Task Sends_the_recording_as_a_form_with_the_model_and_the_key()
    {
        var subject = Start("ok", [1], []);
        using var audio = new MemoryStream([9, 8, 7]);

        var transcript = await subject.Listener.Transcribe(
            subject.Connection,
            subject.ApiKey,
            new TranscriptionRequest(audio, "audio/webm;codecs=opus", "whisper-large-v3", 1024) { Language = "en" },
            TestContext.Current.CancellationToken);

        transcript.Seconds.Should().Be(2.5);
        var request = sent.Should().ContainSingle().Which;
        request.Path.Should().Be("/v1/audio/transcriptions");
        request.ContentType.Should().StartWith("multipart/form-data");
        request.Authorization.Should().Be($"Bearer {subject.ApiKey}");
        request.Body.Should().Contain("name=model").And.Contain("whisper-large-v3")
            .And.Contain("filename=speech.webm").And.Contain("name=language");
    }

    [Fact]
    public async Task Asks_for_speech_with_the_voice_format_and_speed()
    {
        var subject = Start("ok", [1, 2], []);

        await foreach (var chunk in subject.Speaker.Speak(
            subject.Connection,
            null,
            new SpeechRequest("Hello there.", "kokoro", "af_heart", SpeechFormat.Wav) { Speed = 1.25 },
            TestContext.Current.CancellationToken))
        {
            chunk.Length.Should().Be(2);
        }

        var request = sent.Should().ContainSingle().Which;
        request.Authorization.Should().BeNull();
        var body = JsonElement.Parse(request.Body);
        body.GetProperty("model").GetString().Should().Be("kokoro");
        body.GetProperty("input").GetString().Should().Be("Hello there.");
        body.GetProperty("voice").GetString().Should().Be("af_heart");
        body.GetProperty("response_format").GetString().Should().Be("wav");
        body.GetProperty("speed").GetDouble().Should().Be(1.25);
    }

    [Theory]
    [InlineData("https://api.openai.com/v1", "tts-1", "alloy")]
    [InlineData("http://kokoro:8880/v1", "kokoro", "af_heart")]
    [InlineData("https://api.groq.com/openai/v1", "canopylabs/orpheus-v1-english", "autumn")]
    public async Task Falls_back_to_known_voices_when_the_server_lists_none(string address, string model, string first)
    {
        var subject = Start("ok", [1], []);

        var voices = await subject.Speaker.ListVoices(
            new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, new Uri(address)),
            subject.ApiKey,
            model,
            TestContext.Current.CancellationToken);

        voices[0].Id.Should().Be(first);
    }

    [Theory]
    [InlineData("whisper-large-v3-turbo", true, false, false)]
    [InlineData("gpt-transcribe", true, false, false)]
    [InlineData("kokoro", false, true, false)]
    [InlineData("tts-1-hd", false, true, false)]
    [InlineData("gpt-realtime-2.1", false, false, true)]
    [InlineData("grok-voice-latest", false, false, true)]
    [InlineData("llama-3.3-70b", false, false, false)]
    public async Task Marks_speech_models_by_name_when_listing(string model, bool listens, bool speaks, bool live)
    {
        var handler = new ScriptedHandler((_, _, _) => Task.FromResult(
            ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = new[] { new { id = model } } }))));
        var provider = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));

        var listed = await provider.ListModels(
            new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address),
            null,
            TestContext.Current.CancellationToken);

        var capabilities = listed.Should().ContainSingle().Which.Capabilities;
        capabilities.SpeechToText.Should().Be(listens);
        capabilities.TextToSpeech.Should().Be(speaks);
        capabilities.RealtimeVoice.Should().Be(live);
        capabilities.ToolCalling.Should().Be(!(listens || speaks || live));
    }

    #endregion

    #region Private Methods

    private SpeechUnderTest Subject(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var handler = new ScriptedHandler(async (request, _, cancellationToken) =>
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            lock (sent)
            {
                sent.Add((
                    request.RequestUri!.AbsolutePath,
                    request.Content?.Headers.ContentType?.ToString() ?? string.Empty,
                    body,
                    request.Headers.Authorization?.ToString()));
            }

            return answer(request);
        });
        var provider = new OpenAICompatibleProvider(new FixtureHttpClientFactory(handler));
        return new SpeechUnderTest(
            provider,
            provider,
            new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, CompatibleRig.Address),
            TestKeys.Generate(),
            "speech-model",
            () => handler.Count);
    }

    #endregion
}
