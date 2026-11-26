namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Saying a message: the microphone button exists only where something can listen, one press records and one stops,
/// the words land in the message field and are never sent, and each way it can fail is said in its sentence.
/// </summary>
public sealed class VoiceComposerViewModelTests : VoiceTest
{
    #region Private Fields

    private readonly ComposerViewModel composer = new() { BotName = "Sample bot" };

    private readonly List<string> sent = [];

    private readonly List<string> told = [];

    #endregion

    #region Public Methods

    [Fact]
    public async Task With_nothing_that_can_listen_there_is_no_microphone_button()
    {
        Offer(speak: Filled("sample-tts"));
        var model = await Opened();

        model.Shows.Should().BeFalse();
    }

    [Fact]
    public async Task A_device_that_cannot_record_has_no_microphone_button()
    {
        Offer(listen: Filled("sample-whisper"));
        Capture.IsAvailable = false;
        var model = await Opened();

        model.Shows.Should().BeFalse();
    }

    [Fact]
    public async Task One_press_records_and_the_next_puts_the_words_in_the_field_without_sending()
    {
        Offer(listen: Filled("sample-whisper"));
        Server.Answer("POST", $"{Space}/voice/transcribe", new TranscriptResponse("  Sample words from a recording. "));
        var model = await Opened();
        model.Shows.Should().BeTrue();
        model.ButtonLabel.Should().Be("Record a message");

        await model.Press();

        model.Phase.Should().Be(VoicePhase.Recording);
        model.ButtonLabel.Should().Be("Stop recording");
        model.Announcement.Should().Be("Recording");
        model.Elapsed.Should().Be("0:00");
        Time.Advance(TimeSpan.FromSeconds(7));
        model.Elapsed.Should().Be("0:07");

        await model.Press();

        model.Phase.Should().Be(VoicePhase.Idle);
        model.Elapsed.Should().BeEmpty();
        composer.Text.Should().Be("Sample words from a recording.");
        sent.Should().BeEmpty("the transcript is never sent by itself");
        Server.Requests.Should().ContainSingle(request => request.Path.EndsWith("/voice/transcribe", StringComparison.Ordinal));

        composer.Text = $"{composer.Text} And an edit.";
        composer.Text.Should().EndWith("And an edit.");
    }

    [Fact]
    public async Task The_words_go_after_what_was_already_written()
    {
        Offer(listen: Filled("sample-whisper"));
        Server.Answer("POST", $"{Space}/voice/transcribe", new TranscriptResponse("second part"));
        var model = await Opened();
        composer.Text = "First part,";

        await model.Press();
        await model.Press();

        composer.Text.Should().Be("First part, second part");
    }

    [Fact]
    public async Task While_the_server_writes_it_down_the_state_says_so()
    {
        Offer(listen: Filled("sample-whisper"));
        Server.Answer("POST", $"{Space}/voice/transcribe", new TranscriptResponse("Sample words"));
        var held = Server.Hold("POST", $"{Space}/voice/transcribe");
        var model = await Opened();
        await model.Press();

        var stopping = model.Press();

        model.Phase.Should().Be(VoicePhase.Writing);
        model.Announcement.Should().Be("Writing it down");
        held.SetResult();
        await stopping;
        model.Phase.Should().Be(VoicePhase.Idle);
        composer.Text.Should().Be("Sample words");
    }

    [Fact]
    public async Task Discarding_a_recording_sends_nothing_and_leaves_the_field_alone()
    {
        Offer(listen: Filled("sample-whisper"));
        var model = await Opened();
        composer.Text = "Kept";
        await model.Press();

        await model.Discard();

        model.Phase.Should().Be(VoicePhase.Idle);
        Capture.RecordingsDiscarded.Should().Be(1);
        Capture.RecordingsFinished.Should().Be(0);
        composer.Text.Should().Be("Kept");
        Server.Requests.Should().NotContain(request => request.Path.EndsWith("/voice/transcribe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discarding_while_the_words_are_on_their_way_drops_them()
    {
        Offer(listen: Filled("sample-whisper"));
        Server.Answer("POST", $"{Space}/voice/transcribe", new TranscriptResponse("Sample words"));
        Server.Hold("POST", $"{Space}/voice/transcribe");
        var model = await Opened();
        await model.Press();
        var stopping = model.Press();

        await model.Discard();
        await stopping;

        model.Phase.Should().Be(VoicePhase.Idle);
        composer.Text.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AudioCaptureFailure.PermissionRefused, AudioHead.Browser, "Drover is not allowed to use the microphone. Allow it for this site in the browser's site settings, then try again.")]
    [InlineData(AudioCaptureFailure.PermissionRefused, AudioHead.Android, "Drover is not allowed to use the microphone. Allow it for Drover in Android's settings, under Apps and Permissions, then try again.")]
    [InlineData(AudioCaptureFailure.PermissionRefused, AudioHead.Desktop, "Drover is not allowed to use the microphone. Allow it for Drover in the system's privacy settings, then try again.")]
    [InlineData(AudioCaptureFailure.NoMicrophone, AudioHead.Desktop, "This device has no microphone Drover can use. Connect one and try again.")]
    [InlineData(AudioCaptureFailure.Failed, AudioHead.Browser, "The microphone stopped working. Check it and try again.")]
    public async Task A_microphone_that_cannot_be_used_is_said_in_its_sentence(AudioCaptureFailure failure, AudioHead head, string sentence)
    {
        Offer(listen: Filled("sample-whisper"));
        (Capture.Refuses, Capture.Head) = (failure, head);
        var model = await Opened();

        await model.Press();

        model.Phase.Should().Be(VoicePhase.Idle);
        model.Problem.Should().Be(sentence);
        told.Should().Equal(sentence);
    }

    [Fact]
    public async Task A_provider_that_fails_is_said_in_the_servers_sentence_and_the_next_press_clears_it()
    {
        Offer(listen: Filled("sample-whisper"));
        Server.Refuse("POST", $"{Space}/voice/transcribe", HttpStatusCode.BadGateway, "The sample speech provider did not answer. Try again.");
        var model = await Opened();
        await model.Press();

        await model.Press();

        model.Problem.Should().Be("The sample speech provider did not answer. Try again.");
        told.Should().Equal("The sample speech provider did not answer. Try again.");
        composer.Text.Should().BeEmpty();

        await model.Press();
        model.Problem.Should().BeNull();
    }

    [Fact]
    public async Task A_recording_past_the_servers_limit_is_not_sent_and_names_the_limit()
    {
        Offer(listen: Filled("sample-whisper"));
        Capture.Clip = new(new byte[Limits.MaxRecordingBytes!.Value + 1], "audio/wav");
        var model = await Opened();
        await model.Press();

        await model.Press();

        model.Problem.Should().Be("This recording is too long. This server takes recordings up to 25 MB.");
        Server.Requests.Should().NotContain(request => request.Path.EndsWith("/voice/transcribe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_recording_with_no_words_says_so()
    {
        Offer(listen: Filled("sample-whisper"));
        Server.Answer("POST", $"{Space}/voice/transcribe", new TranscriptResponse("  "));
        var model = await Opened();
        await model.Press();

        await model.Press();

        model.Problem.Should().Be("No words were heard in the recording. Record it again.");
    }

    [Fact]
    public async Task The_options_are_read_once_for_the_space()
    {
        Offer(listen: Filled("sample-whisper"));
        await Opened();
        await Opened();

        Server.Requests.Count(request => request.Path.EndsWith("/voice/options", StringComparison.Ordinal)).Should().Be(1);

        await Voice.Refresh();
        Server.Requests.Count(request => request.Path.EndsWith("/voice/options", StringComparison.Ordinal)).Should().Be(2);
    }

    #endregion

    #region Private Methods

    private async Task<VoiceComposerViewModel> Opened()
    {
        composer.OnSend = text =>
        {
            sent.Add(text);
            return Task.FromResult(true);
        };
        var model = new VoiceComposerViewModel(Voice, Api, Session, Capture, Ui, Time, NullLogger<VoiceComposerViewModel>.Instance)
        {
            OnWritten = composer.Dictate,
            OnFailed = told.Add,
        };
        await model.Open();
        return model;
    }

    #endregion
}
