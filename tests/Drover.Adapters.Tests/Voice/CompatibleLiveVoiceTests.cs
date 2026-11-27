namespace Drover.Adapters.Tests.Voice;

/// <summary>
/// The live protocol's adapter against a stand-in server on this machine that speaks that protocol, as its published
/// reference describes it. The clock is the test's, so what counts as a speaker being quiet is decided here.
/// </summary>
public sealed class CompatibleLiveVoiceTests : IAsyncLifetime
{
    #region Private Fields

    private readonly ConcurrentQueue<JsonElement> received = new();

    private readonly IHttpClientFactory clients =
        new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

    private readonly FakeTimeProvider time = new();

    private WebApplication? server;

    private Uri address = null!;

    private string? seenAuthorization;

    private string? seenQuery;

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
        server.Map("/v1/live/sessions", Serve);
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
    public async Task A_call_joins_the_words_into_utterances_hands_work_over_after_them_and_reports_what_it_came_to()
    {
        var socket = new System.Net.WebSockets.ClientWebSocket();
        await socket.ConnectAsync(new Uri($"{address.ToString().Replace("http", "ws", StringComparison.Ordinal)}/live/sessions"), TestContext.Current.CancellationToken);
        await using var session = new LiveVoiceSession(new VoiceSocket(socket), time);
        await session.Start(
            new RealtimeVoiceOptions("gpt-live-1.1", "marin", "You are Atlas. You help plan trips.")
            {
                History = [new RealtimeTurn(false, "Hi"), new RealtimeTurn(true, "Hello Sam.")],
            },
            TestContext.Current.CancellationToken);
        var reader = session.Read(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);

        // The person asks, the model hands the work over before all of the words are in, and acknowledges aloud.
        await session.SendAudio(new byte[] { 1, 0, 2, 0 }, TestContext.Current.CancellationToken);
        var acknowledging = await ReadThroughAudio(reader);
        var afterQuiet = await ReadWhileQuiet(reader, 3);

        // The work is done, and the model says what it came to before the provider closes the session.
        await session.Report("del_1", "Two things are waiting.", TestContext.Current.CancellationToken);
        var answering = await ReadThroughAudio(reader);
        List<RealtimeVoiceEvent> closing = [];

        while (await reader.MoveNextAsync())
        {
            closing.Add(reader.Current);
        }

        await reader.DisposeAsync();

        acknowledging.Should().Equal(new RealtimeBotSaying("One "), new RealtimeBotSaying("moment."));
        afterQuiet.Should().Equal(
            new RealtimePersonSaid("What is waiting for me?"),
            new RealtimeWorkHandedOver("del_1"),
            new RealtimeBotSaid("One moment."));
        answering.Should().Equal(new RealtimeBotSaying("Two things."));
        closing.Should().Equal(new RealtimeBotSaid("Two things."));

        var sent = received.ToArray();
        var start = sent[0].GetProperty("session");
        sent[0].GetProperty("type").GetString().Should().Be("session.start");
        start.GetProperty("model").GetString().Should().Be("gpt-live-1.1");
        start.GetProperty("instructions").GetString().Should().StartWith("You are Atlas. You help plan trips.")
            .And.Contain("simple questions about your name or role")
            .And.Contain("using your configured identity and description")
            .And.Contain("Delegate to the backend for all other questions");
        start.GetProperty("audio").GetProperty("format").GetProperty("type").GetString().Should().Be("audio/pcm");
        start.GetProperty("audio").GetProperty("format").GetProperty("rate").GetInt32().Should().Be(24_000);
        start.GetProperty("audio").GetProperty("output").GetProperty("voice").GetString().Should().Be("marin");
        start.GetProperty("delegation").GetProperty("type").GetString().Should().Be("client");
        start.GetProperty("input")[0].GetProperty("role").GetString().Should().Be("user");
        start.GetProperty("input")[0].GetProperty("content")[0].GetProperty("type").GetString().Should().Be("input_text");
        start.GetProperty("input")[1].GetProperty("content")[0].GetProperty("type").GetString().Should().Be("output_text");
        start.GetProperty("input")[1].GetProperty("content")[0].GetProperty("text").GetString().Should().Be("Hello Sam.");
        sent[1].GetProperty("type").GetString().Should().Be("session.input_audio.append");
        Convert.FromBase64String(sent[1].GetProperty("audio").GetString()!).Should().Equal(1, 0, 2, 0);
        var report = sent[2];
        report.GetProperty("type").GetString().Should().Be("session.commentary.append");
        report.GetProperty("delegation_id").GetString().Should().Be("del_1");
        report.GetProperty("content").GetString().Should().Be("Two things are waiting.");
    }

    [Fact]
    public async Task A_model_of_the_live_protocol_is_called_at_its_own_address_and_a_refused_start_is_a_failure_with_no_provider_text()
    {
        refuse = true;
        var key = TestKeys.Generate();
        var provider = new OpenAICompatibleProvider(clients);

        var call = async () => await provider.Open(
            new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, address),
            key,
            new RealtimeVoiceOptions("gpt-live-1.1", "marin", "x"),
            TestContext.Current.CancellationToken);

        var failure = (await call.Should().ThrowAsync<ModelProviderException>()).Which;
        failure.Failure.Should().Be(ModelFailure.InvalidKey);
        failure.ToString().Should().NotContain("PROVIDER-TEXT").And.NotContain(key);
        seenAuthorization.Should().Be($"Bearer {key}");
        seenQuery.Should().BeEmpty("the model is named in the first message, never in the address");
    }

    [Fact]
    public void A_live_model_is_known_by_its_name_and_hands_its_work_over()
    {
        var provider = new OpenAICompatibleProvider(clients);

        provider.HandsWorkOver("gpt-live-1.1").Should().BeTrue();
        provider.HandsWorkOver("gpt-realtime-2.1").Should().BeFalse();
        provider.ListVoices("gpt-live-1.1")[0].Id.Should().Be("marin");
        KnownVoices.Mark(new ModelCapabilities { ToolCalling = true }, "gpt-live-1.1")
            .Should().Match<ModelCapabilities>(marked => marked.RealtimeVoice && !marked.SpeechToText && !marked.ToolCalling);
        KnownVoices.Mark(new ModelCapabilities(), "gpt-live-transcribe")
            .Should().Match<ModelCapabilities>(marked => marked.SpeechToText && !marked.RealtimeVoice, "it writes speech down and holds no call");
    }

    [Fact]
    public void The_first_generation_of_the_model_is_told_to_hand_over_in_its_own_wording()
    {
        LiveText.Instructions(new RealtimeVoiceOptions("gpt-live-1", "marin", "Voice.")).Should()
            .Contain("Do not generate <|SpawnThinking|> for greetings")
            .And.Contain("Generate <|SpawnThinking|> for all other questions");
        LiveText.Instructions(new RealtimeVoiceOptions("gpt-live-1.1", "marin", "Voice.")).Should().NotContain("SpawnThinking");
    }

    [Fact]
    public void A_report_is_cut_into_pieces_the_provider_takes_each_ending_with_a_sentence()
    {
        var sentence = "This sample sentence is fifty characters long, ok. ";
        var text = string.Concat(Enumerable.Repeat(sentence, 60));

        var pieces = LiveText.Pieces(text, LiveVoiceSession.MaxReportTokens).ToList();

        pieces.Should().HaveCountGreaterThan(1);
        pieces.Should().OnlyContain(piece => LiveText.Tokens(piece) <= LiveVoiceSession.MaxReportTokens && piece.EndsWith('.'));
        string.Join(' ', pieces).Should().Be(text.Trim());
        LiveText.Pieces("Short.", LiveVoiceSession.MaxReportTokens).Should().Equal("Short.");

        // A script the provider counts more tokens for is cut shorter, and never between the halves of one character.
        var other = string.Concat(Enumerable.Repeat("\U0001F600", 400));
        LiveText.Pieces(other, LiveVoiceSession.MaxReportTokens).Should().OnlyContain(piece =>
            LiveText.Tokens(piece) <= LiveVoiceSession.MaxReportTokens && !char.IsHighSurrogate(piece.Last()));
    }

    [Fact]
    public void The_earlier_conversation_is_its_newest_messages_inside_the_providers_limit()
    {
        List<RealtimeTurn> turns = [.. Enumerable.Range(0, 200).Select(at => new RealtimeTurn(at % 2 == 1, $"Sample message {at} {new string('x', 600)}"))];

        var kept = LiveText.History(turns);

        kept.Should().HaveCountLessThan(128).And.NotBeEmpty();
        JsonSerializer.Serialize(kept[^1]).Should().Contain("Sample message 199 ", "the newest message is the last one kept");
        JsonSerializer.Serialize(kept[0]).Should().NotContain("Sample message 0 ");
    }

    #endregion

    #region Reading

    // Everything up to the audio that ends a batch of the stand-in's messages, without the audio.
    private static async Task<List<RealtimeVoiceEvent>> ReadThroughAudio(IAsyncEnumerator<RealtimeVoiceEvent> reader)
    {
        List<RealtimeVoiceEvent> read = [];

        while (await reader.MoveNextAsync() && reader.Current is not RealtimeAudioOut)
        {
            read.Add(reader.Current);
        }

        return read;
    }

    // The next events, with the clock moved on a little at a time until each arrives: nothing else is on its way.
    private async Task<List<RealtimeVoiceEvent>> ReadWhileQuiet(IAsyncEnumerator<RealtimeVoiceEvent> reader, int count)
    {
        List<RealtimeVoiceEvent> read = [];

        while (read.Count < count)
        {
            var next = reader.MoveNextAsync().AsTask();

            while (!next.IsCompleted)
            {
                time.Advance(TimeSpan.FromMilliseconds(50));
                await Task.WhenAny(next, Task.Delay(5, TestContext.Current.CancellationToken));
            }

            (await next).Should().BeTrue();
            read.Add(reader.Current);
        }

        return read;
    }

    #endregion

    #region Stand-in Server

    private async Task Serve(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        seenAuthorization = context.Request.Headers.Authorization.ToString();
        seenQuery = context.Request.QueryString.ToString();
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var buffer = new byte[256 * 1024];

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, context.RequestAborted);

            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            {
                return;
            }

            var message = JsonElement.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
            received.Enqueue(message);

            switch (message.GetProperty("type").GetString())
            {
                case "session.start" when refuse:
                    await Say(socket, """{"type":"error","event_id":"evt_1","error":{"type":"authentication_error","code":"invalid_api_key","message":"PROVIDER-TEXT"}}""", context.RequestAborted);
                    return;

                case "session.start":
                    await Say(socket, """{"type":"session.started","event_id":"evt_1","session":{"id":"live_1","status":"active","expires_at":1788555600}}""", context.RequestAborted);
                    break;

                case "session.input_audio.append":
                    await Say(socket, """{"type":"session.input_transcript.delta","event_id":"evt_2","delta":"What is ","start_ms":100,"end_ms":400}""", context.RequestAborted);
                    await Say(socket, """{"type":"session.delegation.created","event_id":"evt_3","offset_ms":900,"delegation":{"id":"del_1","type":"delegation","target":"client"}}""", context.RequestAborted);
                    await Say(socket, """{"type":"session.input_transcript.delta","event_id":"evt_4","delta":"waiting for me?","start_ms":400,"end_ms":900}""", context.RequestAborted);
                    await Say(socket, """{"type":"session.output_transcript.delta","event_id":"evt_5","delta":"One ","start_ms":1000,"end_ms":1200}""", context.RequestAborted);
                    await Say(socket, """{"type":"session.output_transcript.delta","event_id":"evt_6","delta":"moment.","start_ms":1200,"end_ms":1500}""", context.RequestAborted);
                    await Say(socket, $$"""{"type":"session.output_audio.delta","delta":"{{Convert.ToBase64String([1, 2])}}"}""", context.RequestAborted);
                    break;

                case "session.commentary.append":
                    await Say(socket, """{"type":"session.commentary.appended","event_id":"evt_7","client_event_id":"drover_report_1","start_ms":5200,"end_ms":5400}""", context.RequestAborted);
                    await Say(socket, """{"type":"session.output_transcript.delta","event_id":"evt_8","delta":"Two things.","start_ms":5400,"end_ms":6000}""", context.RequestAborted);
                    await Say(socket, $$"""{"type":"session.output_audio.delta","delta":"{{Convert.ToBase64String([3, 4])}}"}""", context.RequestAborted);
                    await Say(socket, """{"type":"session.usage.updated","event_id":"evt_9","usage":{"seconds":7}}""", context.RequestAborted);
                    await Say(socket, """{"type":"session.closed","event_id":"evt_10","reason":"expired","usage":{"seconds":7}}""", context.RequestAborted);
                    return;
            }
        }
    }

    private static Task Say(System.Net.WebSockets.WebSocket socket, string json, CancellationToken cancellationToken) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), System.Net.WebSockets.WebSocketMessageType.Text, true, cancellationToken);

    #endregion
}
