namespace Drover.Adapters.Tests.Voice;

/// <summary>The realtime adapter against a stand-in server on this machine that speaks the realtime protocol.</summary>
public sealed class CompatibleRealtimeVoiceTests : IAsyncLifetime
{
    #region Private Fields

    private readonly ConcurrentQueue<JsonElement> received = new();

    private readonly IHttpClientFactory clients =
        new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

    private readonly TaskCompletionSource scriptDone = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private WebApplication? server;

    private Uri address = null!;

    private string? seenAuthorization;

    private string? seenModel;

    private bool refuse;

    #endregion

    #region Lifetime

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        server = builder.Build();
        server.UseWebSockets();
        server.Map("/v1/realtime", Serve);
        await server.StartAsync(TestContext.Current.CancellationToken);
        address = new Uri($"{server.Urls.First()}/v1");
    }

    public async ValueTask DisposeAsync()
    {
        if (server is not null)
        {
            await server.DisposeAsync();
        }
    }

    #endregion

    #region Tests

    [Fact]
    public async Task Configures_the_call_and_translates_a_whole_turn()
    {
        var key = TestKeys.Generate();
        await using var session = await Open(key, new RealtimeVoiceOptions("gpt-realtime-2.1", "marin", "You are Atlas.")
        {
            History = [new RealtimeTurn(false, "Hi"), new RealtimeTurn(true, "Hello Sam.")],
            TranscriptionModel = "gpt-transcribe",
        });

        await session.SendAudio(new byte[] { 1, 0, 2, 0 }, TestContext.Current.CancellationToken);
        List<RealtimeVoiceEvent> events = [];

        await foreach (var read in session.Read(TestContext.Current.CancellationToken))
        {
            events.Add(read);
        }

        seenAuthorization.Should().Be($"Bearer {key}");
        seenModel.Should().Be("gpt-realtime-2.1");
        events.Select(read => read.GetType()).Should().Equal(
            typeof(RealtimePersonStartedSpeaking),
            typeof(RealtimePersonSaid),
            typeof(RealtimeAudioOut),
            typeof(RealtimeBotSaying),
            typeof(RealtimeAudioOut),
            typeof(RealtimeBotSaying),
            typeof(RealtimeBotSaid),
            typeof(RealtimeTurnUsage),
            typeof(RealtimeFailed));
        events.OfType<RealtimePersonSaid>().Single().Text.Should().Be("What is waiting for me?");
        events.OfType<RealtimeAudioOut>().SelectMany(audio => audio.Samples.ToArray()).Should().Equal(5, 6, 7, 8);
        events.OfType<RealtimeBotSaid>().Single().Text.Should().Be("Two things.");
        events.OfType<RealtimeTurnUsage>().Single().Should().Be(new RealtimeTurnUsage(120, 45));
        var failed = events.OfType<RealtimeFailed>().Single();
        failed.Failure.Should().Be(ModelFailure.RetryLater);
        failed.Message.Should().NotContain("PROVIDER-TEXT");

        await scriptDone.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var sent = received.ToArray();
        var setup = sent[0];
        setup.GetProperty("type").GetString().Should().Be("session.update");
        var configured = setup.GetProperty("session");
        configured.GetProperty("type").GetString().Should().Be("realtime");
        configured.GetProperty("instructions").GetString().Should().Be("You are Atlas.");
        configured.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString().Should().Be("marin");
        configured.GetProperty("audio").GetProperty("input").GetProperty("format").GetProperty("rate").GetInt32().Should().Be(24_000);
        configured.GetProperty("audio").GetProperty("input").GetProperty("transcription").GetProperty("model").GetString().Should().Be("gpt-transcribe");
        sent[1].GetProperty("item").GetProperty("role").GetString().Should().Be("user");
        sent[2].GetProperty("item").GetProperty("content")[0].GetProperty("type").GetString().Should().Be("output_text");
        sent[3].GetProperty("type").GetString().Should().Be("input_audio_buffer.append");
        Convert.FromBase64String(sent[3].GetProperty("audio").GetString()!).Should().Equal(1, 0, 2, 0);
    }

    [Fact]
    public async Task Interrupting_cancels_the_answer_and_says_how_much_was_heard()
    {
        await using var session = await Open(null, new RealtimeVoiceOptions("gpt-realtime-2.1", "marin", "Short."));
        var reader = session.Read(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        while (await reader.MoveNextAsync() && reader.Current is not RealtimeAudioOut)
        {
        }

        await session.Interrupt(480, TestContext.Current.CancellationToken);
        await session.SendAudio(new byte[] { 3, 0 }, TestContext.Current.CancellationToken);
        await scriptDone.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await reader.DisposeAsync();

        var sent = received.ToArray();
        sent.Should().Contain(message => message.GetProperty("type").GetString() == "response.cancel");
        var truncate = sent.Single(message => message.GetProperty("type").GetString() == "conversation.item.truncate");
        truncate.GetProperty("item_id").GetString().Should().Be("item_1");
        truncate.GetProperty("audio_end_ms").GetInt32().Should().Be(480);
    }

    [Fact]
    public async Task A_refused_handshake_is_an_invalid_key_and_keeps_the_key_out_of_the_failure()
    {
        refuse = true;
        var key = TestKeys.Generate();

        var call = async () => await Open(key, new RealtimeVoiceOptions("gpt-realtime-2.1", "marin", "x"));

        var failure = (await call.Should().ThrowAsync<ModelProviderException>()).Which;
        failure.Failure.Should().BeOneOf(ModelFailure.InvalidKey, ModelFailure.Rejected);
        failure.ToString().Should().NotContain(key);
    }

    [Fact]
    public void Offers_the_known_voices_of_a_live_model()
    {
        var provider = new OpenAICompatibleProvider(clients);

        provider.ListVoices("gpt-realtime-2.1")[0].Id.Should().Be("marin");
    }

    #endregion

    #region Stand-in Server

    private Task<IRealtimeVoiceSession> Open(string? key, RealtimeVoiceOptions options) =>
        new OpenAICompatibleProvider(clients).Open(
            new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, address),
            key,
            options,
            TestContext.Current.CancellationToken);

    private async Task Serve(HttpContext context)
    {
        if (refuse || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        seenAuthorization = context.Request.Headers.Authorization.ToString();
        seenModel = context.Request.Query["model"].ToString();
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[64 * 1024];

        // Everything up to and including the first audio from the microphone.
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, context.RequestAborted);

            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            {
                scriptDone.TrySetResult();
                return;
            }

            var message = JsonElement.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
            received.Enqueue(message);
            var type = message.GetProperty("type").GetString();

            if (type == "session.update")
            {
                // The answer starts at once in the interrupt test, which sends audio only after it has heard some.
                await Say(socket, """{"type":"input_audio_buffer.speech_started","audio_start_ms":10,"item_id":"in_1"}""", context.RequestAborted);
                await Say(socket, """{"type":"conversation.item.input_audio_transcription.completed","item_id":"in_1","transcript":" What is waiting for me? "}""", context.RequestAborted);
                await Say(socket, $$"""{"type":"response.output_audio.delta","item_id":"item_1","delta":"{{Convert.ToBase64String([5, 6])}}"}""", context.RequestAborted);
                await Say(socket, """{"type":"response.output_audio_transcript.delta","delta":"Two "}""", context.RequestAborted);
            }

            if (type == "input_audio_buffer.append")
            {
                break;
            }
        }

        await Say(socket, $$"""{"type":"response.audio.delta","item_id":"item_1","delta":"{{Convert.ToBase64String([7, 8])}}"}""", context.RequestAborted);
        await Say(socket, """{"type":"response.audio_transcript.delta","delta":"things."}""", context.RequestAborted);
        await Say(socket, """{"type":"response.done","response":{"status":"completed","usage":{"input_tokens":120,"output_tokens":45}}}""", context.RequestAborted);
        await Say(socket, """{"type":"error","error":{"type":"rate_limit_error","code":"rate_limit_exceeded","message":"PROVIDER-TEXT"}}""", context.RequestAborted);
        scriptDone.TrySetResult();
        await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
    }

    private static Task Say(System.Net.WebSockets.WebSocket socket, string json, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), System.Net.WebSockets.WebSocketMessageType.Text, true, cancellationToken);

    #endregion
}
