namespace Drover.Server.Tests;

/// <summary>
/// Speech over the API with a stand-in provider: what a space can use, its choices, marking a model, writing down a
/// recording and reading text aloud, each with the refusals that keep one person's and one space's data apart.
/// </summary>
public sealed class VoiceApiTests(PostgresServer postgres) : VoiceTestBase(postgres)
{
    #region Private Fields

    private const string Marker = "zebra-crossing-marker-phrase";

    #endregion

    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:Policy:Rules:MaxVoiceRecordingBytes"] = "4096",
        ["Drover:Policy:Rules:MaxReadAloudCharacters"] = "120",
    };

    #endregion

    #region Options And Settings

    [Fact]
    public async Task A_space_with_no_speech_model_has_nothing_selected_and_one_with_marked_models_gets_them_automatically()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);

        var empty = await Read<VoiceOptionsResponse>(await client.Get($"{Space(alice.SpaceId)}/voice/options"));
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        var options = await Read<VoiceOptionsResponse>(await client.Get($"{Space(alice.SpaceId)}/voice/options"));

        (empty.Listen.Selected, empty.Speak.Selected, empty.Live.Selected).Should().Be((null, null, null));
        empty.Limits.Should().Be(new VoiceLimits(4096, 120, null), "a limit the server does not set is null");
        options.Listen.Sources.Should().Equal(new VoiceSourceResponse(seeded.Connection.Id, "Speech", "whisper-1", false, false));
        options.Listen.Selected.Should().Be(new VoiceSelection(seeded.Connection.Id, "Speech", "whisper-1", null, false, false, true));
        options.Speak.Selected!.Model.Should().Be("tts-1");
        options.Live.Selected.Should().Match<VoiceSelection>(live => live.Model == "realtime-1" && live.Automatic);
    }

    [Fact]
    public async Task An_admin_saves_the_choices_and_a_wrong_one_is_refused_with_a_sentence()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var client = await SignIn(alice.Email, alice.Password);
        var asBob = await SignIn(bob.Email, bob.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        var bobs = await CreateConnection(asBob, bob.SpaceId);
        await SeedModels(bob.SpaceId, bob.UserId, bobs.Id, ("tts-1", new ModelCapabilities { TextToSpeech = true }));
        var path = $"{Space(alice.SpaceId)}/voice/settings";

        var byMember = await asMember.Put(path, new VoiceSettingsRequest(null, null, null));
        var otherSpace = await client.Put(path, new VoiceSettingsRequest(null, new VoiceChoiceBody(bobs.Id, "tts-1"), null));
        var unmarked = await client.Put(path, new VoiceSettingsRequest(null, new VoiceChoiceBody(seeded.Connection.Id, "plain"), null));
        var wrongSlot = await client.Put(path, new VoiceSettingsRequest(new VoiceChoiceBody(seeded.Connection.Id, "tts-1"), null, null));
        var saved = await client.Put(path, new VoiceSettingsRequest(null, new VoiceChoiceBody(seeded.Connection.Id, "tts-1", " verse "), null));
        var read = await Read<VoiceOptionsResponse>(await asMember.Get($"{Space(alice.SpaceId)}/voice/options"));

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        otherSpace.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Sentence(otherSpace)).Should().Be("That connection is not in this space.");
        unmarked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Sentence(unmarked)).Should().Contain("not marked as reading aloud");
        (await Sentence(wrongSlot)).Should().Contain("not marked as writing down speech");
        (await Read<VoiceOptionsResponse>(saved)).Speak.Selected.Should().Be(
            new VoiceSelection(seeded.Connection.Id, "Speech", "tts-1", "verse", false, false, false));
        read.Speak.Selected!.Automatic.Should().BeFalse("the choice was stored");
        read.Listen.Selected!.Automatic.Should().BeTrue("a slot left out is still chosen by the server");

        (await client.Put(path, new VoiceSettingsRequest(null, null, null))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Read<VoiceOptionsResponse>(await client.Get($"{Space(alice.SpaceId)}/voice/options"))).Speak.Selected!.Automatic.Should().BeTrue();
    }

    [Fact]
    public async Task An_admin_marks_a_model_for_speech_and_clears_the_mark()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var client = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var connection = await CreateConnection(client, alice.SpaceId);
        await SeedModels(alice.SpaceId, alice.UserId, connection.Id, ("local-voice", new ModelCapabilities()));
        var path = $"{Space(alice.SpaceId)}/model-connections/{connection.Id}/models/voice";

        var byMember = await asMember.Put(path, new SetModelVoiceRequest("local-voice", false, true, false));
        var unknown = await client.Put(path, new SetModelVoiceRequest("unlisted", false, true, false));
        var marked = await Read<ModelCatalogResponse>(await client.Put(path, new SetModelVoiceRequest("local-voice", false, true, false)));
        var offered = await Read<VoiceOptionsResponse>(await client.Get($"{Space(alice.SpaceId)}/voice/options"));
        var cleared = await Read<ModelCatalogResponse>(await client.Put(path, new SetModelVoiceRequest("local-voice", false, false, false)));

        byMember.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (marked.Capabilities.SpeechToText, marked.Capabilities.TextToSpeech, marked.Capabilities.RealtimeVoice, marked.Capabilities.VoiceSetByOperator)
            .Should().Be((false, true, false, true));
        offered.Speak.Selected!.Model.Should().Be("local-voice");
        (cleared.Capabilities.TextToSpeech, cleared.Capabilities.VoiceSetByOperator).Should().Be((false, false));
    }

    [Fact]
    public async Task The_voices_of_a_model_are_asked_of_its_provider_with_the_connections_key()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        var path = $"{Space(alice.SpaceId)}/voice/voices";

        var voices = await Read<List<VoiceOptionResponse>>(await client.Get($"{path}?connectionId={seeded.Connection.Id}&model=tts-1"));
        var live = await Read<List<VoiceOptionResponse>>(await client.Get($"{path}?connectionId={seeded.Connection.Id}&model=realtime-1"));
        var unmarked = await client.Get($"{path}?connectionId={seeded.Connection.Id}&model=plain");
        var noServer = await client.Get($"{path}?model=kokoro");

        voices.Should().Equal(new VoiceOptionResponse("alloy", "Alloy"), new VoiceOptionResponse("verse", "Verse"));
        live.Should().HaveCount(2);
        unmarked.StatusCode.Should().Be(HttpStatusCode.NotFound);
        noServer.StatusCode.Should().Be(HttpStatusCode.NotFound, "this server has no speech service of its own");
    }

    #endregion

    #region Transcription

    [Fact]
    public async Task A_recording_is_written_down_and_nothing_of_it_is_stored_or_logged()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        Speech.TranscriptText = $"the words were {Marker}";
        var audio = Encoding.UTF8.GetBytes($"RIFF-audio-{Marker}");

        var heard = await client.PostContent($"{Space(alice.SpaceId)}/voice/transcribe", Audio(audio, "audio/webm;codecs=opus"));

        (await Read<TranscriptResponse>(heard)).Text.Should().Be($"the words were {Marker}");
        Speech.Transcriptions.Should().ContainSingle().Which.Should().Be(
            (new Uri("https://speech.example.test/v1"), "sk-test-speech-key", "whisper-1", "audio/webm", audio.Length));
        Logs.AllText().Should().NotContain(Marker, "neither the audio nor its words belong in a log");
        Logs.AllText().Should().NotContain("sk-test-speech-key");
        var files = DroverFactory.FilesPath(KeyRingPath);
        (Directory.Exists(files) ? Directory.EnumerateFiles(files, "*", SearchOption.AllDirectories) : []).Should().BeEmpty("a recording is never written to disk");
        (await CountRows("files")).Should().Be(0);
        (await CountRows("messages")).Should().Be(0);
    }

    [Fact]
    public async Task A_recording_past_the_limit_is_refused_whether_or_not_its_length_is_declared()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        var path = $"{Space(alice.SpaceId)}/voice/transcribe";

        var declared = await client.PostContent(path, Audio(new byte[5000], "audio/wav"));
        var undeclared = await client.PostContent(path, new UnmeasuredContent(new byte[5000], "audio/wav"));
        var fits = await client.PostContent(path, Audio(new byte[4096], "audio/wav"));

        declared.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Sentence(declared)).Should().StartWith("This recording is too long.");
        undeclared.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Sentence(undeclared)).Should().StartWith("This recording is too long.");
        fits.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_recording_is_refused_in_a_format_drover_does_not_take_without_a_model_and_when_the_provider_fails()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var path = $"{Space(alice.SpaceId)}/voice/transcribe";

        var noModel = await client.PostContent(path, Audio([1, 2, 3], "audio/wav"));
        await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        var wrongType = await client.PostContent(path, Audio([1, 2, 3], "video/mp4"));
        Speech.Failure = ModelProviderException.FromStatus(401);
        var failed = await client.PostContent(path, Audio([1, 2, 3], "audio/wav"));

        noModel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Sentence(noModel)).Should().Be("Voice input needs a model that writes down speech. Connect one under Models.");
        wrongType.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        failed.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await Sentence(failed)).Should().StartWith("The provider rejected the API key.");
    }

    #endregion

    #region Read Aloud

    [Fact]
    public async Task A_sample_text_is_streamed_as_audio_in_the_voice_and_format_asked_for()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        var path = $"{Space(alice.SpaceId)}/voice/speak";

        var spoken = await client.Post(path, new SpeakRequest(Text: "Good morning."));
        var chosen = await client.Post(path, new SpeakRequest(Text: "Good morning.", Voice: "verse", Format: "wav"));

        spoken.StatusCode.Should().Be(HttpStatusCode.OK, await spoken.Content.ReadAsStringAsync(Token));
        spoken.Content.Headers.ContentType!.MediaType.Should().Be("audio/mpeg");
        (await spoken.Content.ReadAsStringAsync(Token)).Should().Be("[mp3:alloy:Good morning.]", "the first voice the provider lists is used when none is chosen");
        chosen.Content.Headers.ContentType!.MediaType.Should().Be("audio/wav");
        (await chosen.Content.ReadAsStringAsync(Token)).Should().Be("[wav:verse:Good morning.]");
        Speech.Spoken.First().Key.Should().Be("sk-test-speech-key");
    }

    [Fact]
    public async Task A_message_is_read_aloud_from_its_text_parts_only()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        var reply = new Message
        {
            Role = MessageRole.Assistant,
            AuthorBotId = seeded.Bot.Id,
            CreatedAt = Time.GetUtcNow(),
            Parts =
            [
                new ReasoningPart("private reasoning"),
                new TextPart("First."),
                new ToolResultPart(Guid.CreateVersion7(), "call-1", false, JsonSerializer.SerializeToElement(new { secret = "tool output" })),
                new TextPart("Second."),
            ],
        };
        await SeedMessages(alice.SpaceId, alice.UserId, seeded.Thread.Id, reply);

        var spoken = await client.Post($"{Space(alice.SpaceId)}/voice/speak", new SpeakRequest(reply.Id));

        (await spoken.Content.ReadAsStringAsync(Token)).Should().Be("[mp3:alloy:First.\n\nSecond.]");
        Speech.Spoken.Should().ContainSingle().Which.Request.Text.Should().NotContainAny("reasoning", "tool output");
    }

    [Fact]
    public async Task A_message_in_another_persons_private_conversation_or_another_space_is_not_found()
    {
        var alice = await SeedPerson("alice");
        var eve = await SeedPerson("eve");
        var bob = await SeedMember(alice.SpaceId, "bob", MembershipRole.Admin);
        var client = await SignIn(alice.Email, alice.Password);
        var asBob = await SignIn(bob.Email, bob.Password);
        var asEve = await SignIn(eve.Email, eve.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        await SeedBotWithSpeech(asEve, eve.SpaceId, eve.UserId);
        var message = new Message { Role = MessageRole.User, AuthorUserId = alice.UserId, Parts = [new TextPart("Only for Alice.")], CreatedAt = Time.GetUtcNow() };
        await SeedMessages(alice.SpaceId, alice.UserId, seeded.Thread.Id, message);

        var byBob = await asBob.Post($"{Space(alice.SpaceId)}/voice/speak", new SpeakRequest(message.Id));
        var byEve = await asEve.Post($"{Space(eve.SpaceId)}/voice/speak", new SpeakRequest(message.Id));
        var eveInAlices = await asEve.Post($"{Space(alice.SpaceId)}/voice/speak", new SpeakRequest(message.Id));
        var byAlice = await client.Post($"{Space(alice.SpaceId)}/voice/speak", new SpeakRequest(message.Id));

        byBob.StatusCode.Should().Be(HttpStatusCode.NotFound, "an admin of the space still cannot read another member's private conversation");
        byEve.StatusCode.Should().Be(HttpStatusCode.NotFound, "the message is in another space");
        eveInAlices.StatusCode.Should().Be(HttpStatusCode.NotFound, "Eve is no member of Alice's space");
        byAlice.StatusCode.Should().Be(HttpStatusCode.OK);
        Speech.Spoken.Should().ContainSingle("only Alice's request reached the provider");
    }

    [Fact]
    public async Task Read_aloud_refuses_text_past_the_limit_a_request_that_names_both_or_neither_and_a_failing_provider()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var path = $"{Space(alice.SpaceId)}/voice/speak";

        var noModel = await client.Post(path, new SpeakRequest(Text: "Hello."));
        await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);
        var tooLong = await client.Post(path, new SpeakRequest(Text: new string('a', 121)));
        var neither = await client.Post(path, new SpeakRequest());
        var both = await client.Post(path, new SpeakRequest(Guid.CreateVersion7(), "Hello."));
        var badFormat = await client.Post(path, new SpeakRequest(Text: "Hello.", Format: "flac"));
        Speech.Failure = ModelProviderException.FromStatus(429);
        var failed = await client.Post(path, new SpeakRequest(Text: "Hello.", Voice: "alloy"));

        noModel.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Sentence(noModel)).Should().Be("Reading aloud needs a speech model. Connect one under Models.");
        tooLong.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Sentence(tooLong)).Should().Be("This text is 121 characters. This server reads aloud up to 120.");
        neither.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        badFormat.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        failed.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await Sentence(failed)).Should().StartWith("The provider is limiting requests");
    }

    #endregion

    #region Private Methods

    private static ByteArrayContent Audio(byte[] bytes, string mediaType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);
        return content;
    }

    private async Task<long> CountRows(string table)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync(Token))!;
    }

    /// <summary>A body sent without a declared length, as a recorder that streams while it records sends it.</summary>
    private sealed class UnmeasuredContent : HttpContent
    {
        #region Private Fields

        private readonly byte[] bytes;

        #endregion

        #region Constructors

        public UnmeasuredContent(byte[] bytes, string mediaType)
        {
            this.bytes = bytes;
            Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        }

        #endregion

        #region Overrides

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        #endregion
    }

    #endregion
}
