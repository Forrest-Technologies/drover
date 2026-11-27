namespace Drover.Engine.Tests.Voice;

public sealed class VoiceChoicesTests
{
    #region Private Fields

    private static readonly ServerVoiceOptions noServer = new();

    private static readonly ServerVoiceOptions server = new()
    {
        SpeechAddress = new Uri("http://kokoro:8880/v1"),
        TranscriptionAddress = new Uri("http://speaches:8000/v1"),
        TranscriptionModel = "whisper-small",
    };

    #endregion

    #region Automatic

    [Fact]
    public void With_nothing_chosen_the_first_working_connection_by_name_and_its_first_marked_model_by_name_are_used()
    {
        var beta = Source("Beta", ConnectionStatus.Ready, ("tts-b", Speaks), ("tts-a", Speaks));
        var alpha = Source("alpha", ConnectionStatus.Failed, ("tts-z", Speaks));
        var gamma = Source("Gamma", ConnectionStatus.Pending, ("plain", new ModelCapabilities()), ("whisper", Listens));

        var speak = VoiceChoices.Resolve(VoiceSlot.Speak, null, [beta, alpha, gamma], noServer);
        var listen = VoiceChoices.Resolve(VoiceSlot.Listen, null, [beta, alpha, gamma], noServer);
        var live = VoiceChoices.Resolve(VoiceSlot.Live, null, [beta, alpha, gamma], noServer);

        speak.Should().Match<VoicePick>(pick => pick.Candidate.ConnectionId == beta.Connection.Id && pick.Candidate.Model == "tts-a" && pick.Automatic && pick.Voice == null);
        listen!.Candidate.Model.Should().Be("whisper", "a connection not tested yet has not failed");
        live.Should().BeNull("nothing is marked for live calls");
        VoiceChoices.Candidates(VoiceSlot.Speak, [beta, alpha, gamma], noServer).Select(candidate => candidate.Model)
            .Should().Equal(["tts-z", "tts-a", "tts-b"], "a failed connection is still offered, to be chosen on purpose");
    }

    [Fact]
    public void A_model_that_is_missing_from_the_provider_is_not_offered()
    {
        var source = Source("Speech", ConnectionStatus.Ready, ("tts-old", Speaks));
        source.Catalog[0].State = AvailabilityState.Missing;

        VoiceChoices.Resolve(VoiceSlot.Speak, null, [source], noServer).Should().BeNull();
    }

    [Fact]
    public void The_servers_own_service_is_the_last_resort_for_listening_and_speaking_and_never_for_live_calls()
    {
        var failed = Source("Broken", ConnectionStatus.Failed, ("tts-1", Speaks));

        var speak = VoiceChoices.Resolve(VoiceSlot.Speak, null, [failed], server);
        var listen = VoiceChoices.Resolve(VoiceSlot.Listen, null, [], server);

        speak.Should().Match<VoicePick>(pick => pick.Candidate.IsServer && pick.Candidate.Model == "kokoro" && pick.Voice == "af_heart" && pick.Automatic);
        speak!.Candidate.Kind.Should().Be(ModelConnectionKinds.ServerSpeech);
        listen.Should().Match<VoicePick>(pick => pick.Candidate.IsServer && pick.Candidate.Model == "whisper-small" && pick.Voice == null);
        VoiceChoices.Resolve(VoiceSlot.Live, null, [], server).Should().BeNull();
        VoiceChoices.Resolve(VoiceSlot.Speak, null, [Source("Works", ConnectionStatus.Ready, ("tts-1", Speaks))], server)!.Candidate.IsServer
            .Should().BeFalse("the space's own model comes first");
    }

    #endregion

    #region Chosen

    [Fact]
    public void A_choice_is_kept_while_its_model_is_still_marked_even_on_a_failed_connection_and_dropped_when_it_is_not()
    {
        var first = Source("A", ConnectionStatus.Ready, ("tts-1", Speaks));
        var chosen = Source("B", ConnectionStatus.Failed, ("tts-2", Speaks), ("whisper", Listens));
        var settings = new VoiceSettings
        {
            Speak = new VoiceChoice(chosen.Connection.Id, "tts-2", "nova"),
            Listen = new VoiceChoice(chosen.Connection.Id, "tts-2", null),
            Live = new VoiceChoice(Guid.CreateVersion7(), "gone", null),
        };

        VoiceChoices.Resolve(VoiceSlot.Speak, settings, [first, chosen], noServer)
            .Should().Match<VoicePick>(pick => pick.Candidate.Model == "tts-2" && pick.Voice == "nova" && !pick.Automatic);
        VoiceChoices.Resolve(VoiceSlot.Listen, settings, [first, chosen], noServer)
            .Should().BeNull("the chosen model is not marked for listening, and the only one that is sits on a failed connection");
        VoiceChoices.Resolve(VoiceSlot.Live, settings, [first, chosen], noServer).Should().BeNull();
    }

    #endregion

    #region Addresses

    [Theory]
    [InlineData("http://192.168.1.20:11434/v1", true)]
    [InlineData("http://127.0.0.1:8880/v1", true)]
    [InlineData("http://localhost:8880/v1", true)]
    [InlineData("http://kokoro:8880/v1", true)]
    [InlineData("http://nas.local/v1", true)]
    [InlineData("https://api.openai.com/v1", false)]
    [InlineData("https://8.8.8.8/v1", false)]
    public void An_address_reads_as_private_from_its_text_alone(string address, bool isPrivate) =>
        VoiceChoices.LooksPrivate(new Uri(address)).Should().Be(isPrivate);

    #endregion

    #region Private Methods

    private static ModelCapabilities Speaks => new() { TextToSpeech = true };

    private static ModelCapabilities Listens => new() { SpeechToText = true };

    private static VoiceSource Source(string name, ConnectionStatus status, params (string Model, ModelCapabilities Capabilities)[] models)
    {
        var connection = new ModelConnection
        {
            Name = name,
            Kind = ModelConnectionKinds.Compatible,
            BaseUrl = "https://speech.example.test/v1",
            Status = status,
        };
        return new VoiceSource(
            connection,
            [.. models.Select(model => new ModelCatalogEntry
            {
                ModelConnectionId = connection.Id,
                Model = model.Model,
                DisplayName = model.Model,
                Capabilities = model.Capabilities,
            })]);
    }

    #endregion
}
