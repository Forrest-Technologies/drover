namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Reading a reply aloud: the speaker button exists only where something can speak, one press plays and the next
/// stops, and one reply plays at a time.
/// </summary>
public sealed class ReadAloudViewModelTests : VoiceTest
{
    #region Private Fields

    private static readonly Guid first = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1");

    private static readonly Guid second = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d2");

    #endregion

    #region Public Methods

    [Fact]
    public async Task With_nothing_that_can_speak_there_is_no_speaker_button()
    {
        Offer(listen: Filled("sample-whisper"));
        await Voice.Load();

        Reader.Shows.Should().BeFalse();
    }

    [Fact]
    public async Task A_device_that_cannot_play_has_no_speaker_button()
    {
        Offer(speak: Filled("sample-tts"));
        Playback.IsAvailable = false;
        await Voice.Load();

        Reader.Shows.Should().BeFalse();
    }

    [Fact]
    public async Task A_press_plays_the_reply_in_the_devices_format_and_the_button_becomes_stop()
    {
        await CanSpeak();
        Playback.ClipFormat = "pcm";
        Server.AnswerBytes("POST", $"{Space}/voice/speak", [9, 8, 7], "audio/pcm");

        var reading = Reader.Press(first);
        await Until(() => Playback.ClipPlaying);

        Reader.Shows.Should().BeTrue();
        Reader.IsReading(first).Should().BeTrue();
        Reader.Label(first).Should().Be("Stop reading");
        Reader.Label(second).Should().Be("Read aloud");
        Server.Sent("POST", $"{Space}/voice/speak").Single().Should().Contain(first.ToString()).And.Contain("\"format\":\"pcm\"");
        Playback.Clips.Single().Should().BeEquivalentTo(("audio/pcm", new byte[] { 9, 8, 7 }));

        Playback.EndClip();
        await reading;

        Reader.IsReading(first).Should().BeFalse();
        Reader.Label(first).Should().Be("Read aloud");
    }

    [Fact]
    public async Task Pressing_the_reply_that_is_playing_stops_it()
    {
        await CanSpeak();
        Server.AnswerBytes("POST", $"{Space}/voice/speak", [1], "audio/mpeg");
        var reading = Reader.Press(first);
        await Until(() => Playback.ClipPlaying);

        await Reader.Press(first);
        await reading;

        Playback.ClipsStopped.Should().Be(1);
        Reader.Playing.Should().BeNull();
        Server.Sent("POST", $"{Space}/voice/speak").Should().ContainSingle();
    }

    [Fact]
    public async Task Starting_another_reply_stops_the_first()
    {
        await CanSpeak();
        Server.AnswerBytes("POST", $"{Space}/voice/speak", [1], "audio/mpeg");
        var one = Reader.Press(first);
        await Until(() => Playback.ClipPlaying);

        var two = Reader.Press(second);
        await one;
        await Until(() => Playback.Clips.Count == 2 && Playback.ClipPlaying);

        Playback.ClipsStopped.Should().Be(1);
        Reader.IsReading(first).Should().BeFalse();
        Reader.IsReading(second).Should().BeTrue();

        Playback.EndClip();
        await two;
        Reader.Playing.Should().BeNull();
    }

    [Fact]
    public async Task A_refusal_is_said_in_the_servers_sentence()
    {
        await CanSpeak();
        Server.Refuse("POST", $"{Space}/voice/speak", HttpStatusCode.BadRequest, "This text is 9000 characters. This server reads aloud up to 4000.");

        await Reader.Press(first);

        Reader.Playing.Should().BeNull();
        Toasts.Toasts.Should().ContainSingle(toast => toast.Message == "This text is 9000 characters. This server reads aloud up to 4000.");
    }

    [Fact]
    public async Task A_device_that_cannot_play_the_clip_says_so()
    {
        await CanSpeak();
        Server.AnswerBytes("POST", $"{Space}/voice/speak", [1], "audio/mpeg");
        Playback.FailsToPlay = true;

        await Reader.Press(first);

        Reader.Playing.Should().BeNull();
        Toasts.Toasts.Should().ContainSingle(toast => toast.Message == "This device could not play the sound. Check its sound output and try again.");
    }

    #endregion

    #region Private Methods

    private async Task CanSpeak()
    {
        Offer(speak: Filled("sample-tts", "sample-voice"));
        await Voice.Load();
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var tries = 0; tries < 200 && !condition(); tries++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        condition().Should().BeTrue("the condition should hold once the work is done");
    }

    #endregion
}
