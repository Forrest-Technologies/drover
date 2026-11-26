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
    public async Task A_press_plays_the_reply_at_the_rate_its_file_names_and_the_button_becomes_stop()
    {
        await CanSpeak();
        Server.AnswerBytes("POST", $"{Space}/voice/speak", Wav(22_050, 1, [9, 8, 7, 6]), "audio/wav");

        var reading = Reader.Press(first);
        await Until(() => Playback.Playing);

        Reader.Shows.Should().BeTrue();
        Reader.IsReading(first).Should().BeTrue();
        Reader.Label(first).Should().Be("Stop reading");
        Reader.Label(second).Should().Be("Read aloud");
        Server.Sent("POST", $"{Space}/voice/speak").Single().Should().Contain(first.ToString()).And.Contain("\"format\":\"wav\"");
        Playback.Output!.SampleRate.Should().Be(22_050);
        Playback.Output.Sound.Should().Equal(9, 8, 7, 6);

        Playback.Output.Drain();
        await reading;
        Playback.Output.Disposed.Should().BeTrue("the device's output is let go");

        Reader.IsReading(first).Should().BeFalse();
        Reader.Label(first).Should().Be("Read aloud");
    }

    [Fact]
    public async Task Pressing_the_reply_that_is_playing_stops_it()
    {
        await CanSpeak();
        Server.AnswerBytes("POST", $"{Space}/voice/speak", Wav(24_000, 1, [1, 2]), "audio/wav");
        var reading = Reader.Press(first);
        await Until(() => Playback.Playing);

        await Reader.Press(first);
        await reading;

        Playback.Output!.Flushes.Should().Be(1);
        Reader.Playing.Should().BeNull();
        Server.Sent("POST", $"{Space}/voice/speak").Should().ContainSingle();
    }

    [Fact]
    public async Task Starting_another_reply_stops_the_first()
    {
        await CanSpeak();
        Server.AnswerBytes("POST", $"{Space}/voice/speak", Wav(24_000, 1, [1, 2]), "audio/wav");
        var one = Reader.Press(first);
        await Until(() => Playback.Playing);

        var two = Reader.Press(second);
        await one;
        await Until(() => Playback.Outputs.Count == 2 && Playback.Playing);

        Playback.Outputs[0].Flushes.Should().Be(1);
        Reader.IsReading(first).Should().BeFalse();
        Reader.IsReading(second).Should().BeTrue();

        Playback.Output!.Drain();
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
        Server.AnswerBytes("POST", $"{Space}/voice/speak", Wav(24_000, 1, [1, 2]), "audio/wav");
        Playback.FailsToPlay = true;

        await Reader.Press(first);

        Reader.Playing.Should().BeNull();
        Toasts.Toasts.Should().ContainSingle(toast => toast.Message == "This device could not play the sound. Check its sound output and try again.");
    }

    [Fact]
    public async Task A_clip_that_is_not_plain_sound_says_the_device_could_not_play_it()
    {
        await CanSpeak();
        Server.AnswerBytes("POST", $"{Space}/voice/speak", [0x49, 0x44, 0x33, 4, 0, 0, 0, 0, 0, 0, 0, 0], "audio/mpeg");

        await Reader.Press(first);

        Playback.Outputs.Should().BeEmpty();
        Toasts.Toasts.Should().ContainSingle(toast => toast.Message == "This device could not play the sound. Check its sound output and try again.");
    }

    [Fact]
    public async Task Two_channels_are_played_as_one_and_a_file_with_other_parts_before_its_sound_still_plays()
    {
        await CanSpeak();
        byte[] left100Right300 = [100, 0, 44, 1];
        Server.AnswerBytes("POST", $"{Space}/voice/speak", Wav(48_000, 2, left100Right300, withList: true), "audio/wav");

        var reading = Reader.Press(first);
        await Until(() => Playback.Playing);

        Playback.Output!.SampleRate.Should().Be(48_000);
        Playback.Output.Sound.Should().Equal(200, 0);
        Playback.Output.Drain();
        await reading;
    }

    [Fact]
    public async Task Plain_samples_play_at_the_rate_of_a_call()
    {
        await CanSpeak();
        Server.AnswerBytes("POST", $"{Space}/voice/speak", [5, 6, 7, 8], "audio/pcm");

        var reading = Reader.Press(first);
        await Until(() => Playback.Playing);

        Playback.Output!.SampleRate.Should().Be(24_000);
        Playback.Output.Sound.Should().Equal(5, 6, 7, 8);
        Playback.Output.Drain();
        await reading;
    }

    #endregion

    #region Private Methods

    // A WAV file as a speech provider streams one: sizes it could not know left at their largest.
    private static byte[] Wav(int rate, short channels, byte[] samples, bool withList = false)
    {
        using var file = new MemoryStream();
        using var writer = new BinaryWriter(file);
        writer.Write("RIFF"u8);
        writer.Write(uint.MaxValue);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(rate);
        writer.Write(rate * 2 * channels);
        writer.Write((short)(2 * channels));
        writer.Write((short)16);

        if (withList)
        {
            writer.Write("LIST"u8);
            writer.Write(3);
            writer.Write([1, 2, 3, 0]);
        }

        writer.Write("data"u8);
        writer.Write(uint.MaxValue);
        writer.Write(samples);
        return file.ToArray();
    }

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
