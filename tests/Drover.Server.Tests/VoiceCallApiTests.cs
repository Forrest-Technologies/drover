namespace Drover.Server.Tests;

/// <summary>
/// A live call over the real server's WebSocket with a stand-in speech-to-speech model: what refuses it before the
/// upgrade, the frames in both directions, and the conversation it leaves behind.
/// </summary>
public sealed class VoiceCallApiTests(PostgresServer postgres) : VoiceTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    #endregion

    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:Policy:Rules:MaxCallMinutes"] = "10",
    };

    #endregion

    #region A Call

    [Fact]
    public async Task A_call_carries_audio_both_ways_and_leaves_its_words_in_the_conversation()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        await SeedMessages(
            alice.SpaceId,
            alice.UserId,
            seeded.Thread.Id,
            new Message { Role = MessageRole.User, AuthorUserId = alice.UserId, Parts = [new TextPart("Earlier question.")], CreatedAt = Time.GetUtcNow() },
            new Message { Role = MessageRole.Assistant, AuthorBotId = seeded.Bot.Id, Parts = [new ReasoningPart("hidden"), new TextPart("Earlier answer.")], CreatedAt = Time.GetUtcNow() });

        using var socket = await Call(client, alice.SpaceId, seeded.Thread.Id, origin: "https://localhost");
        var ready = await NextFrame(socket);
        var call = Speech.Calls.Single();
        await socket.SendAsync(new byte[] { 1, 2, 3, 4 }, WebSocketMessageType.Binary, true, Token);
        var heard = await call.Heard.ReadAsync(Token).AsTask().WaitAsync(patience, Token);
        call.Does(
            new RealtimePersonStartedSpeaking(),
            new RealtimePersonSaid("What is the weather?"),
            new RealtimeAudioOut(new byte[] { 9, 8, 7 }),
            new RealtimeBotSaying("Sunny"),
            new RealtimeBotSaid("Sunny all day."),
            new RealtimeTurnUsage(12, 34));
        var started = await NextFrame(socket);
        var personSaid = await NextFrame(socket);
        var audio = await NextAudio(socket);
        var saying = await NextFrame(socket);
        var botSaid = await NextFrame(socket);
        await Send(socket, new VoiceCallFrame(VoiceCallMessages.Interrupt) { HeardMs = 250 });
        var interrupted = await call.Interrupts.ReadAsync(Token).AsTask().WaitAsync(patience, Token);
        call.Does(new RealtimePersonSaid("Thanks, bye."));
        var trailing = await NextFrame(socket);
        await Send(socket, new VoiceCallFrame(VoiceCallMessages.Hangup));
        var ended = await NextFrame(socket);
        var messages = await Read<List<MessageResponse>>(await client.Get($"{Space(alice.SpaceId)}/threads/{seeded.Thread.Id}/messages"));
        var thread = await Read<ThreadSummary>(await client.Get($"{Space(alice.SpaceId)}/threads/{seeded.Thread.Id}"));

        ready.Should().Be(new VoiceCallFrame(VoiceCallMessages.Ready) { Model = "realtime-1", Voice = "alloy", MaxMinutes = 10 });
        (call.Address, call.Key).Should().Be((new Uri("https://speech.example.test/v1"), "sk-test-speech-key"));
        call.Options.Instructions.Should().Be($"Answer briefly.\n\n{VoiceService.SpokenReplyInstruction}");
        call.Options.History.Should().Equal(new RealtimeTurn(false, "Earlier question."), new RealtimeTurn(true, "Earlier answer."));
        call.Options.TranscriptionModel.Should().Be("whisper-1");
        heard.Should().Equal(1, 2, 3, 4);
        started.Type.Should().Be(VoiceCallMessages.PersonStarted);
        (personSaid.Type, personSaid.Text).Should().Be((VoiceCallMessages.PersonSaid, "What is the weather?"));
        audio.Should().Equal(9, 8, 7);
        (saying.Type, saying.Text).Should().Be((VoiceCallMessages.BotSaying, "Sunny"));
        (botSaid.Type, botSaid.Text).Should().Be((VoiceCallMessages.BotSaid, "Sunny all day."));
        interrupted.Should().Be(250);
        trailing.Text.Should().Be("Thanks, bye.");
        (ended.Type, ended.Reason).Should().Be((VoiceCallMessages.Ended, VoiceCallEndReasons.HungUp));
        messages.Select(message => (message.Role, message.Parts.Single(part => part.Kind == "text").Text)).Should().Equal(
            ("user", "Earlier question."),
            ("assistant", "Earlier answer."),
            ("user", "What is the weather?"),
            ("assistant", "Sunny all day."),
            ("user", "Thanks, bye."));
        messages[3].Should().Match<MessageResponse>(reply => reply.RunId == null && reply.Model == "realtime-1");
        thread.ActiveRunId.Should().BeNull("a call starts no run");
        await WaitFor(() => call.HungUp);
        Logs.AllText().Should().NotContainAny("What is the weather?", "Sunny all day.", "sk-test-speech-key");
    }

    [Fact]
    public async Task A_reply_whose_words_arrive_late_is_stored_after_them_and_the_provider_ending_the_call_ends_it()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);

        using var socket = await Call(client, alice.SpaceId, seeded.Thread.Id);
        await NextFrame(socket);
        var call = Speech.Calls.Single();
        call.Does(new RealtimeBotSaid("It is noon."), new RealtimePersonSaid("What time is it?"), new RealtimeBotSaid("Anything else?"));
        call.Ends();
        var frames = await FramesUntilEnded(socket);
        var messages = await Read<List<MessageResponse>>(await client.Get($"{Space(alice.SpaceId)}/threads/{seeded.Thread.Id}/messages"));

        frames[^1].Reason.Should().Be(VoiceCallEndReasons.Provider);
        messages.Select(message => (message.Role, message.Parts.Single().Text)).Should().Equal(
            ("user", "What time is it?"),
            ("assistant", "It is noon."),
            ("assistant", "Anything else?"));
    }

    [Fact]
    public async Task A_call_ends_at_the_servers_time_limit()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);

        using var socket = await Call(client, alice.SpaceId, seeded.Thread.Id);
        await NextFrame(socket);
        Time.Advance(TimeSpan.FromMinutes(11));
        var frames = await FramesUntilEnded(socket);

        frames[^1].Reason.Should().Be(VoiceCallEndReasons.TimeLimit);
    }

    [Fact]
    public async Task A_run_that_starts_during_a_call_ends_the_call_instead_of_mixing_the_two()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);

        using var socket = await Call(client, alice.SpaceId, seeded.Thread.Id);
        await NextFrame(socket);
        await SeedActiveRun(alice.SpaceId, alice.UserId, seeded.Thread.Id, seeded.Bot.Id);
        Speech.Calls.Single().Does(new RealtimePersonSaid("Hello?"), new RealtimeBotSaid("Hi."));
        var frames = await FramesUntilEnded(socket);
        var messages = await Read<List<MessageResponse>>(await client.Get($"{Space(alice.SpaceId)}/threads/{seeded.Thread.Id}/messages"));

        frames.Should().Contain(frame => frame.Type == VoiceCallMessages.Failed && frame.Message == VoiceCallSession.RunStarted);
        frames[^1].Reason.Should().Be(VoiceCallEndReasons.Server);
        messages.Should().BeEmpty("the run is the conversation's one writer");
    }

    [Fact]
    public async Task The_typed_client_holds_a_call_and_reports_a_refusal_with_the_servers_sentence()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        var api = new DroverApi(NewRelay(client)) { AntiforgeryToken = client.Cookie("XSRF-TOKEN") };
        Task<WebSocket> Connect(Uri address, CancellationToken cancellationToken)
        {
            var sockets = NewSocketClient();
            sockets.ConfigureRequest = request => request.Headers.Cookie = client.CookieHeader;
            return sockets.ConnectAsync(address, cancellationToken);
        }

        await using var call = await api.OpenCall(alice.SpaceId, seeded.Thread.Id, Connect, Token);
        await using var events = call.Read(Token).GetAsyncEnumerator(Token);
        async Task<VoiceCallEvent> Next() => await events.MoveNextAsync().AsTask().WaitAsync(patience, Token) ? events.Current : throw new EndOfStreamException();
        var ready = await Next();
        var model = Speech.Calls.Single();
        await call.SendAudio(new byte[] { 5, 6 }, Token);
        var heard = await model.Heard.ReadAsync(Token).AsTask().WaitAsync(patience, Token);
        model.Does(new RealtimePersonSaid("Hello."), new RealtimeAudioOut(new byte[] { 7 }), new RealtimeBotSaid("Hi there."));
        var personSaid = await Next();
        var audio = await Next();
        var botSaid = await Next();
        await call.Interrupt(120, Token);
        var interrupted = await model.Interrupts.ReadAsync(Token).AsTask().WaitAsync(patience, Token);
        await call.HangUp(Token);
        var ended = await Next();
        await SeedActiveRun(alice.SpaceId, alice.UserId, seeded.Thread.Id, seeded.Bot.Id);
        var refused = () => api.OpenCall(alice.SpaceId, seeded.Thread.Id, Connect, Token);

        ready.Should().Be(new VoiceCallReady("realtime-1", "alloy", 10));
        heard.Should().Equal(5, 6);
        personSaid.Should().Be(new VoiceCallPersonSaid("Hello."));
        audio.Should().BeOfType<VoiceCallAudio>().Which.Samples.ToArray().Should().Equal(7);
        botSaid.Should().Be(new VoiceCallBotSaid("Hi there."));
        interrupted.Should().Be(120);
        ended.Should().Be(new VoiceCallEnded(VoiceCallEndReasons.HungUp));
        (await events.MoveNextAsync()).Should().BeFalse("the events end with the call");
        (await refused.Should().ThrowAsync<DroverApiException>()).Which.Should().Match<DroverApiException>(problem =>
            problem.StatusCode == HttpStatusCode.Conflict && problem.Message == VoiceService.BotIsWorking);
        Speech.Calls.Should().ContainSingle("the refused call never reached the provider");
    }

    #endregion

    #region Refusals

    [Fact]
    public async Task A_call_is_refused_before_the_upgrade_with_a_sentence()
    {
        var alice = await SeedPerson("alice");
        var eve = await SeedPerson("eve");
        var bob = await SeedMember(alice.SpaceId, "bob", MembershipRole.Admin);
        var client = await SignIn(alice.Email, alice.Password);
        var asBob = await SignIn(bob.Email, bob.Password);
        var asEve = await SignIn(eve.Email, eve.Password);
        var connection = await CreateConnection(client, alice.SpaceId);
        await SeedModels(alice.SpaceId, alice.UserId, connection.Id, ("plain", new ModelCapabilities()));
        var bot = await Read<BotSummary>(await client.Post($"{Space(alice.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, "plain")));
        var thread = await Read<ThreadSummary>(await client.Put($"{Space(alice.SpaceId)}/bots/{bot.Id}/main-thread"));
        var path = $"{Space(alice.SpaceId)}/threads/{thread.Id}/call";

        var noModel = await client.Get(path);
        await SeedModels(alice.SpaceId, alice.UserId, connection.Id, ("realtime-1", new ModelCapabilities { RealtimeVoice = true }));
        var unknown = await client.Get($"{Space(alice.SpaceId)}/threads/{Guid.CreateVersion7()}/call");
        var byBob = await asBob.Get(path);
        var byEve = await asEve.Get(path);
        var eveInOwnSpace = await asEve.Get($"{Space(eve.SpaceId)}/threads/{thread.Id}/call");
        var signedOut = await NewClient().Get(path);
        var notASocket = await client.Get(path);
        var badOrigin = await client.Get(path, "Origin", "https://elsewhere.example");
        var badOriginSocket = async () => await Call(client, alice.SpaceId, thread.Id, origin: "https://elsewhere.example");
        await SeedActiveRun(alice.SpaceId, alice.UserId, thread.Id, bot.Id);
        var working = await client.Get(path);

        noModel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Sentence(noModel)).Should().Be("A live call needs a model that holds live calls. Connect one under Models.");
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        byBob.StatusCode.Should().Be(HttpStatusCode.NotFound, "another member's private conversation is not found, even for an admin");
        byEve.StatusCode.Should().Be(HttpStatusCode.NotFound, "Eve is no member of the space");
        eveInOwnSpace.StatusCode.Should().Be(HttpStatusCode.NotFound, "the conversation is in another space");
        signedOut.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        notASocket.StatusCode.Should().Be(HttpStatusCode.UpgradeRequired, "the call would have been let in, and nothing was opened for a plain request");
        badOrigin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Sentence(badOrigin)).Should().Be("A live call can only be opened from Drover itself.");
        (await badOriginSocket.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("403");
        working.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Sentence(working)).Should().Be(VoiceService.BotIsWorking);
        Speech.Calls.Should().BeEmpty("no refused request reached the provider");
    }

    [Fact]
    public async Task A_paused_bot_and_a_provider_that_refuses_are_answered_before_the_upgrade()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        Speech.Failure = ModelProviderException.FromStatus(401);

        var refused = async () => await Call(client, alice.SpaceId, seeded.Thread.Id);
        (await refused.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("502");

        Speech.Failure = null;
        await InSpace(alice.SpaceId, alice.UserId, async services =>
            (await services.GetRequiredService<DroverDbContext>().Bots.SingleAsync(bot => bot.Id == seeded.Bot.Id, Token)).Status = BotStatus.Paused);
        var paused = await client.Get($"{Space(alice.SpaceId)}/threads/{seeded.Thread.Id}/call");

        paused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Sentence(paused)).Should().Be("This bot is paused or removed, so it cannot answer.");
    }

    #endregion

    #region Private Methods

    private async Task<WebSocket> Call(ApiClient client, Guid spaceId, Guid threadId, string? origin = null)
    {
        var sockets = NewSocketClient();
        sockets.ConfigureRequest = request =>
        {
            request.Headers.Cookie = client.CookieHeader;

            if (origin is not null)
            {
                request.Headers.Origin = origin;
            }
        };
        return await sockets.ConnectAsync(new Uri($"wss://localhost{Space(spaceId)}/threads/{threadId}/call"), Token);
    }

    private static Task Send(WebSocket socket, VoiceCallFrame frame) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(frame, json), WebSocketMessageType.Text, true, Token);

    private static async Task<(WebSocketMessageType Type, byte[] Bytes)> Next(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        var received = await socket.ReceiveAsync(buffer, Token).WaitAsync(patience, Token);
        return (received.MessageType, buffer[..received.Count]);
    }

    private static async Task<VoiceCallFrame> NextFrame(WebSocket socket)
    {
        var (type, bytes) = await Next(socket);
        type.Should().Be(WebSocketMessageType.Text, "a text frame was expected");
        return JsonSerializer.Deserialize<VoiceCallFrame>(bytes, json)!;
    }

    private static async Task<byte[]> NextAudio(WebSocket socket)
    {
        var (type, bytes) = await Next(socket);
        type.Should().Be(WebSocketMessageType.Binary);
        return bytes;
    }

    private static async Task<List<VoiceCallFrame>> FramesUntilEnded(WebSocket socket)
    {
        List<VoiceCallFrame> frames = [];

        while (frames.Count == 0 || frames[^1].Type != VoiceCallMessages.Ended)
        {
            var (type, bytes) = await Next(socket);
            type.Should().NotBe(WebSocketMessageType.Close, "the call says why it ended before it closes");

            if (type is WebSocketMessageType.Text)
            {
                frames.Add(JsonSerializer.Deserialize<VoiceCallFrame>(bytes, json)!);
            }
        }

        return frames;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            (waited.Elapsed < patience).Should().BeTrue("the condition should come true in time");
            await Task.Delay(20, Token);
        }
    }

    /// <summary>A run that is queued on the conversation, as a message sent from another device would leave one.</summary>
    private Task SeedActiveRun(Guid spaceId, Guid userId, Guid threadId, Guid botId) =>
        InSpace(spaceId, userId, services =>
        {
            var id = Guid.CreateVersion7();
            services.GetRequiredService<DroverDbContext>().Runs.Add(new Run
            {
                Id = id,
                RootRunId = id,
                ThreadId = threadId,
                BotId = botId,
                PrincipalUserId = userId,
                Status = RunStatus.Waiting,
                WaitingSince = Time.GetUtcNow(),
            });
            return Task.CompletedTask;
        });

    #endregion
}
