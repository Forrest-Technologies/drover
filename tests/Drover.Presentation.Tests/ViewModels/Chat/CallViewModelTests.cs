namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// A live call, against a scripted server side: each state has its word, talking over the bot stops its voice at
/// once and tells the server how much was heard, the microphone is ignored for a moment where the device cancels no
/// echo, mute sends nothing, and a call that cannot start says why in the server's sentence.
/// </summary>
public sealed class CallViewModelTests : VoiceTest
{
    #region Private Fields

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000e1");

    private static readonly BotSummary bot = new(
        Guid.Parse("0196f1a0-0000-7000-8000-0000000000e2"), "Sample bot", "sample-bot", "Sample instructions", ConnectionId, "sample-model", "active");

    private static readonly string callPath = $"{Space}/threads/{threadId}/call";

    private readonly ScriptedCall line = new();

    private readonly List<string> announced = [];

    private Uri? connectedTo;

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_call_button_shows_where_something_can_listen_or_hold_a_call()
    {
        Offer(speak: Filled("sample-tts"));
        var model = await Opened();
        model.OffersCall.Should().BeFalse("nothing listens and nothing holds a call");

        Offer(listen: Filled("sample-whisper"));
        await Voice.Refresh();
        model.OffersCall.Should().BeTrue("the microphone button shows, so Call does");

        Offer(live: Filled("sample-realtime"));
        await Voice.Refresh();
        model.OffersCall.Should().BeTrue();

        Capture.IsAvailable = false;
        model.OffersCall.Should().BeFalse("this device cannot record");
    }

    [Fact]
    public async Task Without_a_live_model_the_press_says_what_is_missing_and_starts_nothing()
    {
        Offer(listen: Filled("sample-whisper"));
        Server.Refuse("GET", callPath, HttpStatusCode.Conflict, "Calls need a live voice model or a speech model.");
        var model = await Opened();

        await model.Start(bot, threadId);

        model.State.Should().Be(CallState.NotStarted);
        model.StateWord.Should().BeEmpty();
        model.Problem.Should().Be("Calls need a live voice model or a speech model.");
        model.NeedsModel.Should().BeTrue();
        model.InCall.Should().BeFalse();
        connectedTo.Should().BeNull("nothing is opened");
        Capture.Listening.Should().BeFalse("the microphone is not asked for");

        model.Close();
        model.Active.Should().BeFalse();
    }

    [Fact]
    public async Task A_bot_still_working_refuses_the_call_in_the_servers_sentence_without_a_link_to_models()
    {
        Offer(live: Filled("sample-realtime"));
        Server.Refuse("GET", callPath, HttpStatusCode.Conflict, "The bot is still working in this conversation. Call when it has finished.");
        var model = await Opened();

        await model.Start(bot, threadId);

        model.Problem.Should().Be("The bot is still working in this conversation. Call when it has finished.");
        model.NeedsModel.Should().BeFalse();
    }

    [Fact]
    public async Task Each_state_has_its_word_and_is_announced_once()
    {
        var model = await Connected();
        model.State.Should().Be(CallState.Connecting);
        model.StateWord.Should().Be("Connecting");
        connectedTo!.Scheme.Should().Be("wss");
        connectedTo.AbsolutePath.Should().Be(callPath);

        line.Say(new(VoiceCallMessages.Ready) { Model = "sample-realtime", Voice = "sample-voice" });
        await Until(() => model.State is CallState.Listening);
        model.Elapsed.Should().Be("0:00");
        Time.Advance(TimeSpan.FromSeconds(65));
        model.Elapsed.Should().Be("1:05");

        line.Say(new(VoiceCallMessages.PersonStarted));
        line.Say(new(VoiceCallMessages.PersonSaid) { Text = "Sample question" });
        await Until(() => model.State is CallState.Thinking);
        model.PersonWords.Should().Be("Sample question");

        line.Say(new(VoiceCallMessages.BotSaying) { Text = "Sample " });
        line.Play(1, 2, 3, 4);
        line.Say(new(VoiceCallMessages.BotSaying) { Text = "answer" });
        await Until(() => model.BotWords == "Sample answer");
        model.State.Should().Be(CallState.Speaking);
        Playback.Call!.Queued.Should().ContainSingle();

        line.Say(new(VoiceCallMessages.BotSaid) { Text = "Sample answer." });
        await Until(() => model.BotWords == "Sample answer.");
        model.State.Should().Be(CallState.Speaking, "its voice is still playing");

        Playback.Call.Drain();
        model.State.Should().Be(CallState.Listening);

        model.ToggleMute();
        model.State.Should().Be(CallState.Muted);
        model.StateWord.Should().Be("Muted");
        model.MuteLabel.Should().Be("Unmute");
        model.ToggleMute();

        await model.HangUp();
        model.State.Should().Be(CallState.Ended);
        model.StateWord.Should().Be("Call ended");
        model.Problem.Should().BeNull();
        line.FramesSent.Select(frame => frame.Type).Should().Equal(VoiceCallMessages.Hangup);
        Capture.Listening.Should().BeFalse();
        Playback.Call.Disposed.Should().BeTrue();

        // The clock moving on is not a change of state: each word was said once, when the state changed to it.
        announced.Should().Equal("Connecting", "Listening", "Thinking", "Speaking", "Listening", "Muted", "Listening", "Call ended");
    }

    [Fact]
    public async Task Talking_over_the_bot_stops_its_voice_at_once_and_says_how_much_was_heard()
    {
        var model = await Ready();
        Playback.Call!.Play(5000);
        line.Play(1, 2);
        line.Say(new(VoiceCallMessages.BotSaying) { Text = "A long sample answer" });
        await Until(() => model.State is CallState.Speaking);
        Playback.Call.Play(1240);
        var before = Time.GetUtcNow();

        line.Say(new(VoiceCallMessages.PersonStarted));
        await Until(() => line.FramesSent.Count == 1);

        Playback.Call.Flushes.Should().Be(1);
        (Time.GetUtcNow() - before).Should().BeLessThan(TimeSpan.FromMilliseconds(200), "no clock time passes before the voice stops");
        line.FramesSent.Single().Should().BeEquivalentTo(new VoiceCallFrame(VoiceCallMessages.Interrupt) { HeardMs = 1240 });
        model.State.Should().Be(CallState.Listening);

        // What is left of the answer that was cut off is not played.
        line.Play(3, 4);
        line.Say(new(VoiceCallMessages.BotSaid) { Text = "A long sample" });
        line.Say(new(VoiceCallMessages.PersonSaid) { Text = "Sample interruption" });
        await Until(() => model.State is CallState.Thinking);
        Playback.Call.Queued.Should().ContainSingle();

        // The next answer plays, and is measured from its own start.
        line.Play(5, 6);
        await Until(() => Playback.Call.Queued.Count == 2);
        Playback.Call.Play(300);
        line.Say(new(VoiceCallMessages.PersonStarted));
        await Until(() => line.FramesSent.Count == 2);
        line.FramesSent[1].HeardMs.Should().Be(300);
    }

    [Fact]
    public async Task The_person_starting_to_speak_while_the_bot_is_silent_interrupts_nothing()
    {
        var model = await Ready();

        line.Say(new(VoiceCallMessages.PersonStarted));
        line.Say(new(VoiceCallMessages.PersonSaid) { Text = "Sample words" });
        await Until(() => model.State is CallState.Thinking);

        line.FramesSent.Should().BeEmpty();
        Playback.Call!.Flushes.Should().Be(0);
    }

    [Fact]
    public async Task What_the_microphone_hears_is_sent_and_mute_sends_nothing()
    {
        var model = await Ready();

        Capture.Hear(1, 2);
        await Until(() => line.AudioSent.Count == 1);
        line.AudioSent.Single().Should().Equal(1, 2);

        model.ToggleMute();
        Capture.Hear(3, 4);
        model.ToggleMute();
        Capture.Hear(5, 6);
        await Until(() => line.AudioSent.Count == 2);

        line.AudioSent.Select(sent => sent[0]).Should().Equal(1, 5);
    }

    [Fact]
    public async Task Where_the_device_cancels_no_echo_the_microphone_is_ignored_for_a_moment_after_the_bot_starts()
    {
        Capture.CancelsEcho = false;
        var model = await Ready();
        Capture.Hear(1);
        await Until(() => line.AudioSent.Count == 1);

        line.Play(9, 9);
        await Until(() => model.State is CallState.Speaking);
        Capture.Hear(2);
        Time.Advance(TimeSpan.FromMilliseconds(599));
        Capture.Hear(3);
        Time.Advance(TimeSpan.FromMilliseconds(1));
        Capture.Hear(4);
        await Until(() => line.AudioSent.Count == 2);

        line.AudioSent.Select(sent => sent[0]).Should().Equal(1, 4);

        // A piece that follows while the voice is already playing starts no new guard.
        line.Play(9, 9);
        await Until(() => Playback.Call!.Queued.Count == 2);
        Capture.Hear(5);
        await Until(() => line.AudioSent.Count == 3);
    }

    [Fact]
    public async Task Where_the_device_cancels_echo_the_microphone_is_never_ignored()
    {
        var model = await Ready();

        line.Play(9, 9);
        await Until(() => model.State is CallState.Speaking);
        Capture.Hear(2);

        await Until(() => line.AudioSent.Count == 1);
    }

    [Theory]
    [InlineData(AudioCaptureFailure.PermissionRefused, "Drover is not allowed to use the microphone. Allow it for this site in the browser's site settings, then try again.")]
    [InlineData(AudioCaptureFailure.NoMicrophone, "This device has no microphone Drover can use. Connect one and try again.")]
    public async Task A_microphone_that_cannot_be_used_ends_the_call_before_it_starts(AudioCaptureFailure failure, string sentence)
    {
        Capture.Refuses = failure;
        var model = await Connected();

        model.State.Should().Be(CallState.NotStarted);
        model.Problem.Should().Be(sentence);
        model.NeedsModel.Should().BeFalse();
        line.State.Should().Be(WebSocketState.Closed, "the call is let go");
    }

    [Theory]
    [InlineData(VoiceCallEndReasons.TimeLimit, 30, "The call reached this server's limit of 30 minutes. Call again to go on.")]
    [InlineData(VoiceCallEndReasons.Provider, null, "The speech provider ended the call. Call again to go on.")]
    [InlineData(VoiceCallEndReasons.Server, null, "The call was disconnected. Call again to go on.")]
    public async Task A_call_the_server_ends_says_why(string reason, int? maxMinutes, string sentence)
    {
        var model = await Connected();
        line.Say(new(VoiceCallMessages.Ready) { Model = "sample-realtime", Voice = "sample-voice", MaxMinutes = maxMinutes });
        await Until(() => model.State is CallState.Listening);

        line.Say(new(VoiceCallMessages.Ended) { Reason = reason });
        await Until(() => model.State is CallState.Ended);

        model.Problem.Should().Be(sentence);
        Capture.Listening.Should().BeFalse();
    }

    [Fact]
    public async Task A_failure_during_the_call_is_shown_and_the_call_goes_on()
    {
        var model = await Ready();

        line.Say(new(VoiceCallMessages.Failed) { Message = "The sample provider dropped a reply." });
        await Until(() => model.Problem is not null);

        model.Problem.Should().Be("The sample provider dropped a reply.");
        model.InCall.Should().BeTrue();
    }

    [Fact]
    public async Task A_dropped_connection_ends_the_call()
    {
        var model = await Ready();

        line.Drop();
        await Until(() => model.State is CallState.Ended);

        model.Problem.Should().Be("The call was disconnected. Call again to go on.");
    }

    [Fact]
    public async Task The_words_are_hidden_until_asked_for_and_the_surface_shrinks_to_a_bar()
    {
        var model = await Ready();

        model.WordsShown.Should().BeFalse();
        model.WordsLabel.Should().Be("Show words");
        model.ToggleWords();
        model.WordsShown.Should().BeTrue();
        model.WordsLabel.Should().Be("Hide words");

        model.Shrunk.Should().BeFalse();
        model.ToggleShrunk();
        model.Shrunk.Should().BeTrue();
        model.ShrinkLabel.Should().Be("Expand the call");

        // Pressing Call again during a call brings the surface back; it starts no second call.
        await model.Start(bot, threadId);
        model.Shrunk.Should().BeFalse();
        Server.Requests.Count(request => request.Path == callPath).Should().Be(1);
    }

    #endregion

    #region Private Methods

    private async Task<CallViewModel> Opened()
    {
        var model = new CallViewModel(
            Voice, Api, Session, Capture, Playback, new CallConnection(Connect), Ui, Time, NullLogger<CallViewModel>.Instance);
        var last = string.Empty;
        model.PropertyChanged += (_, _) =>
        {
            if (model.StateWord != last)
            {
                last = model.StateWord;
                if (last.Length > 0)
                {
                    announced.Add(last);
                }
            }
        };
        await model.Open();
        return model;
    }

    // The server would take the call, and the socket is the scripted one.
    private async Task<CallViewModel> Connected()
    {
        Offer(listen: Filled("sample-whisper"), speak: Filled("sample-tts"), live: Filled("sample-realtime"));
        Server.Answer("GET", callPath, HttpStatusCode.UpgradeRequired);
        var model = await Opened();
        await model.Start(bot, threadId);
        return model;
    }

    private async Task<CallViewModel> Ready()
    {
        var model = await Connected();
        line.Say(new(VoiceCallMessages.Ready) { Model = "sample-realtime", Voice = "sample-voice" });
        await Until(() => model.State is CallState.Listening);
        return model;
    }

    private Task<WebSocket> Connect(Uri address, CancellationToken cancellationToken)
    {
        connectedTo = address;
        return Task.FromResult<WebSocket>(line);
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var tries = 0; tries < 300 && !condition(); tries++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        condition().Should().BeTrue("the condition should hold once the call has taken it in");
    }

    #endregion
}
