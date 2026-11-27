namespace Drover.Adapters.Tests.Voice;

public sealed class GeminiRealtimeVoiceTests
{
    #region Private Fields

    private static readonly string[] chatMethods = ["generateContent"];

    private static readonly string[] liveMethods = ["bidiGenerateContent"];

    private static readonly string[] embeddingMethods = ["embedContent"];

    #endregion

    #region Tests

    [Fact]
    public async Task Sends_key_context_and_audio_and_reads_the_transcript_with_interruption()
    {
        var cancellation = TestContext.Current.CancellationToken;
        var key = TestKeys.Generate();
        var exchanges = new TaskCompletionSource<(string? Key, string Query, JsonElement Setup, JsonElement History, JsonElement Audio)>();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var server = builder.Build();
        server.UseWebSockets();
        server.Map(GeminiProvider.LivePath, context => Exchange(context, exchanges));
        await server.StartAsync(cancellation);
        var fixture = new Uri(server.Urls.Single());
        var clients = new ServiceCollection();
        clients.AddGeminiProvider();
        clients.AddHttpClient(GeminiProvider.LiveHttpClientName)
            .AddHttpMessageHandler(() => new FixtureSocketHandler(fixture));
        await using var services = clients.BuildServiceProvider();
        var provider = services.GetRequiredService<IRealtimeVoice>();
        var connection = new ModelEndpoint(GeminiProvider.ConnectionKind, GeminiProvider.BaseAddress);
        var options = new RealtimeVoiceOptions("sample-live", "Kore", "Speak as the test bot.")
        {
            History = [new(false, "What is on my calendar?"), new(true, "A meeting at noon.")],
        };

        await using var call = await provider.Open(connection, key, options, cancellation);
        await call.SendAudio(new byte[] { 1, 0, 2, 0 }, cancellation);
        List<RealtimeVoiceEvent> received = [];

        await foreach (var happened in call.Read(cancellation))
        {
            received.Add(happened);
        }

        var sent = await exchanges.Task.WaitAsync(cancellation);
        sent.Key.Should().Be(key);
        sent.Query.Should().BeEmpty();
        sent.Setup.GetProperty("model").GetString().Should().Be("models/sample-live");
        sent.Setup.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()
            .Should().Be(options.Instructions);
        sent.Setup.GetProperty("generationConfig").GetProperty("speechConfig").GetProperty("voiceConfig")
            .GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString().Should().Be("Kore");
        sent.Setup.GetProperty("historyConfig").GetProperty("initialHistoryInClientContent").GetBoolean().Should().BeTrue();
        sent.History.GetProperty("turnComplete").GetBoolean().Should().BeTrue();
        sent.History.GetProperty("turns").EnumerateArray().Select(turn => turn.GetProperty("role").GetString())
            .Should().Equal("user", "model");
        sent.Audio.GetProperty("mimeType").GetString().Should().Be("audio/pcm;rate=24000");
        sent.Audio.GetProperty("data").GetBytesFromBase64().Should().Equal(1, 0, 2, 0);
        received.Select(happened => happened.GetType()).Should().Equal(
            typeof(RealtimePersonSaid),
            typeof(RealtimeBotSaying),
            typeof(RealtimeAudioOut),
            typeof(RealtimePersonStartedSpeaking),
            typeof(RealtimeBotSaid),
            typeof(RealtimeTurnUsage));
        received.OfType<RealtimePersonSaid>().Single().Text.Should().Be("What time?");
        received.OfType<RealtimeBotSaid>().Single().Text.Should().Be("At noon.");
        received.OfType<RealtimeAudioOut>().Single().Samples.ToArray().Should().Equal(5, 6);
        received.OfType<RealtimeTurnUsage>().Single().Should().Be(new RealtimeTurnUsage(50, 12));
    }

    [Fact]
    public async Task Catalog_marks_only_bidirectional_models_for_calls_and_keeps_the_chat_model()
    {
        var script = new GeminiScript(ScriptedAnswer.Json(new
        {
            models = new object[]
            {
                new { name = "models/chat", supportedGenerationMethods = chatMethods },
                new { name = "models/live", supportedGenerationMethods = liveMethods },
                new { name = "models/embedding", supportedGenerationMethods = embeddingMethods },
            },
        }));

        var provider = script.Provider;
        var models = await provider.ListModels(new(GeminiProvider.ConnectionKind, GeminiProvider.BaseAddress),
            TestKeys.Generate(), TestContext.Current.CancellationToken);

        models.Select(model => model.Id).Should().Equal("chat", "live");
        models[0].Capabilities.RealtimeVoice.Should().BeFalse();
        models[1].Capabilities.RealtimeVoice.Should().BeTrue();
        models[1].Traits.Should().Contain("voice-only");
        provider.ListVoices("live").Should().Contain(voice => voice.Id == "Kore");
    }

    #endregion

    #region Private Methods

    private static async Task Exchange(
        HttpContext context,
        TaskCompletionSource<(string? Key, string Query, JsonElement Setup, JsonElement History, JsonElement Audio)> exchanges)
    {
        var cancellation = context.RequestAborted;
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var setup = await Receive(socket, "setup", cancellation);
        await Send(socket, """{"setupComplete":{}}""", cancellation);
        var history = await Receive(socket, "clientContent", cancellation);
        var audio = await Receive(socket, "realtimeInput", cancellation);
        exchanges.SetResult((context.Request.Headers["x-goog-api-key"].ToString(), context.Request.QueryString.Value ?? "",
            setup, history, audio.GetProperty("audio")));

        await Send(socket, """{"serverContent":{"inputTranscription":{"text":"What "}}}""", cancellation);
        await Send(socket, """{"serverContent":{"inputTranscription":{"text":"time?","finished":true}}}""", cancellation);
        await Send(socket, """
            {"serverContent":{"outputTranscription":{"text":"At noon."},
            "modelTurn":{"parts":[{"inlineData":{"mimeType":"audio/pcm;rate=24000","data":"BQY="}}]}}}
            """, cancellation);
        await Send(socket, """{"voiceActivity":{"type":"ACTIVITY_START"}}""", cancellation);
        await Send(socket, """{"serverContent":{"interrupted":true}}""", cancellation);
        await Send(socket, """{"serverContent":{"turnComplete":true},"usageMetadata":{"promptTokenCount":50,"responseTokenCount":12}}""", cancellation);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellation);
    }

    private static async Task<JsonElement> Receive(WebSocket socket, string member, CancellationToken cancellation)
    {
        var bytes = new byte[4096];
        var part = await socket.ReceiveAsync(bytes.AsMemory(), cancellation);
        using var document = JsonDocument.Parse(bytes.AsMemory(0, part.Count));
        return document.RootElement.GetProperty(member).Clone();
    }

    private static ValueTask Send(WebSocket socket, string json, CancellationToken cancellation) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json).AsMemory(), WebSocketMessageType.Binary, true, cancellation);

    #endregion

    #region Nested Types

    private sealed class FixtureSocketHandler(Uri address) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(address, request.RequestUri!.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }
    }

    #endregion
}
