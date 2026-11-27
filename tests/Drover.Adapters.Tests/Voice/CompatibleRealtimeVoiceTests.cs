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

    // The first audio of the answer: one second of it where a test measures a cut against it.
    private byte[] firstAudio = [5, 6];

    // The stand-in goes on listening once its answer is done, and refuses the cut the way the provider refuses one.
    private bool lingers;

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
        await using var session = await Open(key, new RealtimeVoiceOptions("gpt-realtime-2.1", "marin", "You are Atlas. You help plan trips.")
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
        configured.GetProperty("instructions").GetString().Should().Be("You are Atlas. You help plan trips.");
        configured.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString().Should().Be("marin");
        configured.GetProperty("audio").GetProperty("input").GetProperty("format").GetProperty("rate").GetInt32().Should().Be(24_000);
        configured.GetProperty("audio").GetProperty("input").GetProperty("transcription").GetProperty("model").GetString().Should().Be("gpt-transcribe");

        // A server that is not the protocol's own is asked only for what every server of the protocol takes.
        configured.GetProperty("audio").GetProperty("input").GetProperty("turn_detection").GetProperty("type").GetString().Should().Be("server_vad");
        configured.GetProperty("audio").GetProperty("input").TryGetProperty("noise_reduction", out _).Should().BeFalse();
        sent[1].GetProperty("item").GetProperty("role").GetString().Should().Be("user");
        sent[2].GetProperty("item").GetProperty("content")[0].GetProperty("type").GetString().Should().Be("output_text");
        sent[2].GetProperty("item").GetProperty("content")[0].GetProperty("text").GetString().Should().Be("Hello Sam.");
        sent[3].GetProperty("type").GetString().Should().Be("input_audio_buffer.append");
        Convert.FromBase64String(sent[3].GetProperty("audio").GetString()!).Should().Equal(1, 0, 2, 0);
    }

    [Fact]
    public async Task Interrupting_an_answer_still_being_made_cancels_it_and_says_how_much_was_heard()
    {
        firstAudio = new byte[RealtimeAudio.SampleRate * RealtimeAudio.BytesPerSample];
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
        sent.Should().ContainSingle(message => message.GetProperty("type").GetString() == "response.cancel");
        var truncate = sent.Single(message => message.GetProperty("type").GetString() == "conversation.item.truncate");
        truncate.GetProperty("item_id").GetString().Should().Be("item_1");
        truncate.GetProperty("audio_end_ms").GetInt32().Should().Be(480);
    }

    [Fact]
    public async Task A_cut_never_reaches_past_the_audio_the_provider_sent()
    {
        firstAudio = new byte[RealtimeAudio.SampleRate * RealtimeAudio.BytesPerSample];
        await using var session = await Open(null, new RealtimeVoiceOptions("gpt-realtime-2.1", "marin", "Short."));
        var reader = session.Read(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        while (await reader.MoveNextAsync() && reader.Current is not RealtimeAudioOut)
        {
        }

        // The loudspeaker's count runs ahead of the provider's: the provider refuses a cut past what it sent.
        await session.Interrupt(5000, TestContext.Current.CancellationToken);
        await session.Interrupt(5000, TestContext.Current.CancellationToken);
        await session.SendAudio(new byte[] { 3, 0 }, TestContext.Current.CancellationToken);
        await scriptDone.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await reader.DisposeAsync();

        // One second was sent, so that is the most that can have been heard. The item is cut once.
        var truncate = received.Single(message => message.GetProperty("type").GetString() == "conversation.item.truncate");
        truncate.GetProperty("audio_end_ms").GetInt32().Should().Be(1000);
    }

    [Fact]
    public async Task Interrupting_an_answer_the_provider_already_ended_cancels_nothing_and_its_refusal_is_no_failure()
    {
        lingers = true;
        await using var session = await Open(null, new RealtimeVoiceOptions("gpt-realtime-2.1", "marin", "Short."));
        await session.SendAudio(new byte[] { 1, 0 }, TestContext.Current.CancellationToken);
        var reader = session.Read(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        // The answer is done, and its last sound is still playing when the person talks over it.
        while (await reader.MoveNextAsync() && reader.Current is not RealtimeTurnUsage)
        {
        }

        await session.Interrupt(0, TestContext.Current.CancellationToken);
        List<RealtimeVoiceEvent> after = [];

        while (await reader.MoveNextAsync())
        {
            after.Add(reader.Current);
        }

        await scriptDone.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        received.Should().NotContain(message => message.GetProperty("type").GetString() == "response.cancel");
        received.Should().ContainSingle(message => message.GetProperty("type").GetString() == "conversation.item.truncate");
        after.Should().BeEmpty("the provider's answer to an interruption that came too late is not a failure of the call");
    }

    [Fact]
    public async Task The_service_the_protocol_comes_from_is_asked_for_turns_by_meaning_and_a_cleaned_microphone()
    {
        var socket = new System.Net.WebSockets.ClientWebSocket();
        await socket.ConnectAsync(new Uri($"{address.ToString().Replace("http", "ws", StringComparison.Ordinal)}/realtime"), TestContext.Current.CancellationToken);
        await using var session = new RealtimeVoiceSession(new VoiceSocket(socket), RealtimeDialect.For(new Uri("https://api.openai.com/v1")));

        await session.Configure(new RealtimeVoiceOptions("gpt-realtime-2.1", "marin", "Short."), TestContext.Current.CancellationToken);
        await session.SendAudio(new byte[] { 1, 0 }, TestContext.Current.CancellationToken);
        await scriptDone.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var input = received.First().GetProperty("session").GetProperty("audio").GetProperty("input");
        input.GetProperty("turn_detection").GetProperty("type").GetString().Should().Be("semantic_vad");
        input.GetProperty("turn_detection").GetProperty("interrupt_response").GetBoolean().Should().BeTrue();
        input.GetProperty("noise_reduction").GetProperty("type").GetString().Should().Be("far_field");
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
                await Say(socket, """{"type":"response.created","response":{"status":"in_progress"}}""", context.RequestAborted);
                await Say(socket, $$"""{"type":"response.output_audio.delta","item_id":"item_1","delta":"{{Convert.ToBase64String(firstAudio)}}"}""", context.RequestAborted);
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

        if (lingers)
        {
            await RefuseTheCut(socket, buffer, context.RequestAborted);
        }
        else
        {
            await Say(socket, """{"type":"error","error":{"type":"rate_limit_error","code":"rate_limit_exceeded","message":"PROVIDER-TEXT"}}""", context.RequestAborted);
        }

        scriptDone.TrySetResult();
        await socket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
    }

    // Takes what the client sends until it cuts the answer, and answers the cut as the provider answers one it refuses.
    private async Task RefuseTheCut(System.Net.WebSockets.WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            {
                return;
            }

            var message = JsonElement.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
            received.Enqueue(message);

            if (message.GetProperty("type").GetString() == "conversation.item.truncate")
            {
                var answered = message.GetProperty("event_id").GetString();
                await Say(socket, $$"""{"type":"error","error":{"type":"invalid_request_error","code":"invalid_value","message":"PROVIDER-TEXT","event_id":"{{answered}}"} }""", cancellationToken);
                await Say(socket, """{"type":"error","error":{"type":"invalid_request_error","code":"response_cancel_not_active","message":"PROVIDER-TEXT"}}""", cancellationToken);
                return;
            }
        }
    }

    private static Task Say(System.Net.WebSockets.WebSocket socket, string json, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), System.Net.WebSockets.WebSocketMessageType.Text, true, cancellationToken);

    #endregion
}
