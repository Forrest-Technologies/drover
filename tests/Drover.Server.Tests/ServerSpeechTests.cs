namespace Drover.Server.Tests;

/// <summary>
/// The server's own speech service, named by the operator in configuration: it is offered to every space as "this
/// server", it is what a space without a speech model of its own falls back to, and a space's own model comes first.
/// </summary>
public sealed class ServerSpeechTests(PostgresServer postgres) : VoiceTestBase(postgres)
{
    #region Private Fields

    private readonly FakeSpeech own = new() { Kind = ModelConnectionKinds.ServerSpeech, TranscriptText = "from the server's own service" };

    #endregion

    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [VoiceServiceExtensions.SpeechAddressSetting] = "http://kokoro:8880/v1",
        [VoiceServiceExtensions.TranscriptionAddressSetting] = "http://speaches:8000/v1",
        [VoiceServiceExtensions.TranscriptionModelSetting] = "whisper-small",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        // The real adapter was registered because the addresses are set; it is taken out with the others.
        services.Should().Contain(service => service.ServiceType == typeof(Drover.Adapters.OpenAICompatible.OperatorSpeech));
        base.ConfigureServices(services);
        services.AddSingleton<ISpeechToText>(own);
        services.AddSingleton<ITextToSpeech>(own);
    }

    #endregion

    #region Tests

    [Fact]
    public async Task A_space_with_no_speech_model_listens_and_speaks_through_the_servers_own_service()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);

        var options = await Read<VoiceOptionsResponse>(await client.Get($"{Space(alice.SpaceId)}/voice/options"));
        var spoken = await client.Post($"{Space(alice.SpaceId)}/voice/speak", new SpeakRequest(Text: "Good morning."));
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        var heard = await client.PostContent($"{Space(alice.SpaceId)}/voice/transcribe", content);
        var voices = await Read<List<VoiceOptionResponse>>(await client.Get($"{Space(alice.SpaceId)}/voice/voices?model=kokoro"));

        options.Speak.Selected.Should().Be(new VoiceSelection(null, "This server", "kokoro", "af_heart", true, true, true));
        options.Listen.Selected.Should().Be(new VoiceSelection(null, "This server", "whisper-small", null, true, true, true));
        options.Live.Selected.Should().BeNull("the server's own service holds no live calls");
        (await spoken.Content.ReadAsStringAsync(Token)).Should().Be("[mp3:af_heart:Good morning.]");
        own.Spoken.Single().Should().Match<(Uri Address, string? Key, SpeechRequest Request)>(asked =>
            asked.Address == new Uri("http://kokoro:8880/v1") && asked.Key == null && asked.Request.Model == "kokoro");
        (await Read<TranscriptResponse>(heard)).Text.Should().Be("from the server's own service");
        own.Transcriptions.Single().Address.Should().Be(new Uri("http://speaches:8000/v1"));
        voices.Should().HaveCount(2);
        Speech.Spoken.Should().BeEmpty();
    }

    [Fact]
    public async Task A_spaces_own_speech_model_comes_before_the_servers()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var seeded = await SeedBotWithSpeech(client, alice.SpaceId, alice.UserId);

        var options = await Read<VoiceOptionsResponse>(await client.Get($"{Space(alice.SpaceId)}/voice/options"));
        await client.Post($"{Space(alice.SpaceId)}/voice/speak", new SpeakRequest(Text: "Good morning."));

        options.Speak.Selected!.ConnectionId.Should().Be(seeded.Connection.Id);
        options.Speak.Sources.Select(source => source.IsServer).Should().Equal(false, true);
        Speech.Spoken.Should().ContainSingle();
        own.Spoken.Should().BeEmpty();
    }

    [Fact]
    public void A_speech_address_that_is_not_one_or_a_transcription_address_without_a_model_stops_the_server_with_the_setting_named()
    {
        var notAnAddress = () => VoiceServiceExtensions.ReadServerVoice(Configuration((VoiceServiceExtensions.SpeechAddressSetting, "kokoro:8880")));
        var withCredentials = () => VoiceServiceExtensions.ReadServerVoice(Configuration((VoiceServiceExtensions.SpeechAddressSetting, "http://user:pw@kokoro:8880/v1")));
        var noModel = () => VoiceServiceExtensions.ReadServerVoice(Configuration((VoiceServiceExtensions.TranscriptionAddressSetting, "http://speaches:8000/v1")));
        var none = VoiceServiceExtensions.ReadServerVoice(Configuration());
        var named = VoiceServiceExtensions.ReadServerVoice(Configuration(
            (VoiceServiceExtensions.SpeechAddressSetting, "http://kokoro:8880/v1"),
            (VoiceServiceExtensions.SpeechModelSetting, "tts-local"),
            (VoiceServiceExtensions.SpeechVoiceSetting, "bm_george")));

        notAnAddress.Should().Throw<InvalidOperationException>().WithMessage($"{VoiceServiceExtensions.SpeechAddressSetting}*");
        withCredentials.Should().Throw<InvalidOperationException>();
        noModel.Should().Throw<InvalidOperationException>().WithMessage($"*{VoiceServiceExtensions.TranscriptionModelSetting}*");
        (none.SpeechAddress, none.TranscriptionAddress, none.SpeechModel, none.SpeechVoice).Should().Be((null, null, "kokoro", "af_heart"));
        (named.SpeechModel, named.SpeechVoice).Should().Be(("tts-local", "bm_george"));
    }

    #endregion

    #region Private Methods

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value)).Build();

    #endregion
}
