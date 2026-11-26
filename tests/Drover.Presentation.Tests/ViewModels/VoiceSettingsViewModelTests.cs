namespace Drover.Presentation.Tests.ViewModels;

/// <summary>
/// The Voice page: three rows that say Automatic until someone picks, models listed under their connection, a voice
/// to pick and hear where the slot speaks, the limits behind Advanced, rows a member only reads, and one sentence
/// with a way forward when nothing is connected.
/// </summary>
public sealed class VoiceSettingsViewModelTests : VoiceTest
{
    #region Private Fields

    private static readonly Guid otherConnection = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c2");

    private static readonly string voices = $"{Space}/voice/voices";

    #endregion

    #region Constructors

    public VoiceSettingsViewModelTests()
        : base("owner")
    {
    }

    #endregion

    #region Public Methods

    [Fact]
    public async Task Each_row_says_automatic_until_someone_picks_and_says_what_it_uses()
    {
        Offer(
            listen: Filled("sample-whisper", onNetwork: true),
            speak: new([new(null, "This server", "sample-kokoro", true, false)], new(null, "This server", "sample-kokoro", "sample-voice", true, false, true)),
            live: Filled("sample-realtime", "sample-live-voice"));
        Server.Answer("GET", voices, new List<VoiceOptionResponse> { new("sample-voice", "Sample voice"), new("other-voice", "Other voice") });
        var model = await Opened();

        model.NothingConnected.Should().BeFalse();
        model.Rows.Select(row => row.Title).Should().Equal("Listens with", "Speaks with", "Live calls with");
        model.Rows.Should().OnlyContain(row => row.Model == VoiceSettingsViewModel.AutomaticValue);
        model.Rows.Should().OnlyContain(row => row.Models[0].Label == "Automatic");

        var listen = model.Rows[0];
        listen.Uses.Should().Be("Automatic: sample-whisper, Sample speech (On your network)");
        listen.Models[1].Should().Be(new ChoiceOption($"{ConnectionId}|sample-whisper", "sample-whisper (On your network)", "Sample speech"));
        listen.Voices.Should().BeEmpty("listening has no voice");

        var speak = model.Rows[1];
        speak.Uses.Should().Be("Automatic: sample-kokoro, This server");
        speak.Models.Should().ContainSingle("the server's own speech service is used when nothing is picked, and is not a pick");
        speak.Voices.Select(voice => voice.Label).Should().Equal("Sample voice", "Other voice");
        speak.Voice.Should().Be("sample-voice");
        speak.CanSample.Should().BeTrue();
        speak.CanChooseVoice.Should().BeFalse("the server's own voice is the operator's to set");

        var live = model.Rows[2];
        live.CanChooseVoice.Should().BeTrue();
        live.CanSample.Should().BeFalse();
    }

    [Fact]
    public async Task Models_are_listed_by_connection_then_by_name()
    {
        Offer(listen: new(
            [
                new(otherConnection, "Zeta speech", "b-transcribe", false, false),
                new(ConnectionId, "Alpha speech", "z-whisper", false, false),
                new(ConnectionId, "Alpha speech", "a-whisper", false, false),
            ],
            new(ConnectionId, "Alpha speech", "a-whisper", null, false, false, true)));
        var model = await Opened();

        model.Rows[0].Models.Skip(1).Select(option => $"{option.Group}/{option.Label}")
            .Should().Equal("Alpha speech/a-whisper", "Alpha speech/z-whisper", "Zeta speech/b-transcribe");
    }

    [Fact]
    public async Task Picking_a_model_saves_that_slot_and_leaves_the_others_to_the_server()
    {
        Offer(listen: Filled("sample-whisper"), speak: Filled("sample-tts"));
        Server.Answer("GET", voices, new List<VoiceOptionResponse>());
        Server.Answer("PUT", $"{Space}/voice/settings", new VoiceOptionsResponse(
            Filled("sample-whisper", automatic: false), Filled("sample-tts"), Empty, Limits));
        var model = await Opened();

        await model.ChooseModel(VoiceSlot.Listen, $"{ConnectionId}|sample-whisper");

        Server.Sent("PUT", $"{Space}/voice/settings").Single().Should().Be(
            $$"""{"listen":{"connectionId":"{{ConnectionId}}","model":"sample-whisper","voice":null},"speak":null,"live":null}""");
        model.Rows[0].Model.Should().Be($"{ConnectionId}|sample-whisper");
        model.Rows[0].Uses.Should().Be("sample-whisper, Sample speech");
        model.SaveProblem.Should().BeNull();
    }

    [Fact]
    public async Task Going_back_to_automatic_clears_the_slot_and_keeps_the_others_chosen()
    {
        Offer(listen: Filled("sample-whisper", automatic: false), speak: Filled("sample-tts", "sample-voice", automatic: false));
        Server.Answer("GET", voices, new List<VoiceOptionResponse> { new("sample-voice", "Sample voice") });
        Server.Answer("PUT", $"{Space}/voice/settings", new VoiceOptionsResponse(
            Filled("sample-whisper"), Filled("sample-tts", "sample-voice", automatic: false), Empty, Limits));
        var model = await Opened();

        await model.ChooseModel(VoiceSlot.Listen, VoiceSettingsViewModel.AutomaticValue);

        Server.Sent("PUT", $"{Space}/voice/settings").Single().Should().Be(
            $$"""{"listen":null,"speak":{"connectionId":"{{ConnectionId}}","model":"sample-tts","voice":"sample-voice"},"live":null}""");
        model.Rows[0].Model.Should().Be(VoiceSettingsViewModel.AutomaticValue);
    }

    [Fact]
    public async Task Picking_a_voice_saves_it_with_the_model_in_use()
    {
        Offer(speak: Filled("sample-tts", "sample-voice"));
        Server.Answer("GET", voices, new List<VoiceOptionResponse> { new("sample-voice", "Sample voice"), new("other-voice", "Other voice") });
        Server.Answer("PUT", $"{Space}/voice/settings", new VoiceOptionsResponse(
            Empty, Filled("sample-tts", "other-voice", automatic: false), Empty, Limits));
        var model = await Opened();

        await model.ChooseVoice(VoiceSlot.Speak, "other-voice");

        Server.Sent("PUT", $"{Space}/voice/settings").Single().Should().Contain("\"voice\":\"other-voice\"").And.Contain("\"model\":\"sample-tts\"");
        model.Rows[1].Voice.Should().Be("other-voice");
    }

    [Fact]
    public async Task A_refused_save_says_why_and_the_row_goes_back()
    {
        Offer(listen: Filled("sample-whisper"));
        Server.Refuse("PUT", $"{Space}/voice/settings", HttpStatusCode.BadRequest, "That model is not marked for listening.");
        var model = await Opened();

        await model.ChooseModel(VoiceSlot.Listen, $"{ConnectionId}|sample-whisper");

        model.SaveProblem.Should().Be("That model is not marked for listening.");
        model.Rows[0].Model.Should().Be(VoiceSettingsViewModel.AutomaticValue);
    }

    [Fact]
    public async Task A_member_reads_the_rows_and_cannot_change_them()
    {
        using var asMember = new MemberPage();
        var model = await asMember.Opened();

        model.CanEdit.Should().BeFalse();
        model.Rows.Should().HaveCount(3);
        model.Rows[1].CanChooseVoice.Should().BeFalse();
        model.Rows[1].CanSample.Should().BeTrue("anyone may hear a voice");

        await model.ChooseModel(VoiceSlot.Listen, $"{ConnectionId}|sample-whisper");
        await model.ChooseVoice(VoiceSlot.Speak, "other-voice");

        asMember.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task Play_a_sample_reads_a_sample_text_in_the_chosen_voice_and_stops_on_the_next_press()
    {
        Offer(speak: Filled("sample-tts", "sample-voice"));
        Server.Answer("GET", voices, new List<VoiceOptionResponse> { new("sample-voice", "Sample voice") });
        Server.AnswerBytes("POST", $"{Space}/voice/speak", [1, 2], "audio/pcm");
        var model = await Opened();
        model.SampleLabel.Should().Be("Play a sample");

        var playing = model.PlaySample();
        await Eventually(() => Playback.Playing);

        model.SamplePlaying.Should().BeTrue();
        model.SampleLabel.Should().Be("Stop the sample");
        Server.Sent("POST", $"{Space}/voice/speak").Single().Should().Be(
            """{"messageId":null,"text":"This is a sample of this voice.","voice":"sample-voice","format":"wav"}""");

        await model.PlaySample();
        await playing;
        model.SamplePlaying.Should().BeFalse();
        Playback.Output!.Flushes.Should().Be(1);
    }

    [Fact]
    public async Task Advanced_holds_the_limits_in_effect()
    {
        Offer(listen: Filled("sample-whisper"));
        var model = await Opened();

        model.Limits.Should().Equal("Longest recording: 25 MB", "Longest text read aloud: 4,000 characters", "Longest call: No limit");
    }

    [Fact]
    public async Task With_nothing_connected_the_page_says_voice_needs_a_speech_model()
    {
        Offer();
        var model = await Opened();

        model.NothingConnected.Should().BeTrue();
        model.Rows.Should().BeEmpty();
        VoiceWords.NeedsModel.Should().Be("Voice needs a speech model.");
        VoiceWords.ConnectOne.Should().Be("Connect one");
    }

    [Fact]
    public async Task A_failed_load_says_so_and_loads_on_the_next_try()
    {
        Server.Answer("GET", "/api/session", Samples.Session());
        var model = Model();

        await model.Open();

        model.LoadProblem.Should().NotBeNull();
        model.Loading.Should().BeFalse();

        Offer(listen: Filled("sample-whisper"));
        await model.Load();
        model.LoadProblem.Should().BeNull();
        model.Rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task A_device_without_sound_says_so_and_still_shows_the_spaces_settings()
    {
        Offer(listen: Filled("sample-whisper"), speak: Filled("sample-tts"));
        Server.Answer("GET", voices, new List<VoiceOptionResponse>());
        Capture.IsAvailable = Playback.IsAvailable = false;
        var model = await Opened();

        model.DeviceNote.Should().Be("Voice is not available in this app on this device yet. These settings still apply to the space.");
        model.Rows.Should().HaveCount(3);
        model.Rows[1].CanSample.Should().BeFalse();
    }

    #endregion

    #region Private Methods

    private VoiceSettingsViewModel Model() =>
        new(Api, Session, Voice, Reader, Capture, Playback, NullLogger<VoiceSettingsViewModel>.Instance);

    private async Task<VoiceSettingsViewModel> Opened()
    {
        Server.Answer("GET", "/api/session", Samples.Session());
        var model = Model();
        await model.Open();
        return model;
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var tries = 0; tries < 200 && !condition(); tries++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        condition().Should().BeTrue("the condition should hold once the work is done");
    }

    #endregion

    #region Nested Types

    // The same page, signed in as a member of the space.
    private sealed class MemberPage() : VoiceTest("member")
    {
        #region Properties

        public IEnumerable<string> Saves => Server.Sent("PUT", $"{Space}/voice/settings");

        #endregion

        #region Public Methods

        public async Task<VoiceSettingsViewModel> Opened()
        {
            Server.Answer("GET", "/api/session", Samples.Session("member"));
            Offer(listen: Filled("sample-whisper"), speak: Filled("sample-tts", "sample-voice"), live: Filled("sample-realtime"));
            Server.Answer("GET", $"{Space}/voice/voices", new List<VoiceOptionResponse> { new("sample-voice", "Sample voice"), new("other-voice", "Other voice") });
            var model = new VoiceSettingsViewModel(Api, Session, Voice, Reader, Capture, Playback, NullLogger<VoiceSettingsViewModel>.Instance);
            await model.Open();
            return model;
        }

        #endregion
    }

    #endregion
}
