namespace Drover.Presentation.Tests.Chat;

/// <summary>
/// A message recorded on a device without an encoder: what the microphone hands over becomes a WAV file in memory,
/// and a recording thrown away lets the microphone go and keeps nothing. Every sample is made up.
/// </summary>
public sealed class PcmRecordingTests
{
    #region Private Fields

    private readonly FakeCapture capture = new();

    #endregion

    #region Public Methods

    [Fact]
    public async Task What_the_microphone_hears_becomes_a_wav_file_of_the_calls_sound()
    {
        var recording = await PcmRecording.Start(capture.Listen, TestContext.Current.CancellationToken);
        capture.Listening.Should().BeTrue();
        capture.Hear(1, 2);
        capture.Hear(3, 4);

        var clip = await recording.Finish(TestContext.Current.CancellationToken);

        capture.Listening.Should().BeFalse("the microphone is let go once the message is finished");
        clip.MediaType.Should().Be("audio/wav");
        var file = clip.Audio.Span;
        Encoding.ASCII.GetString(file[..4]).Should().Be("RIFF");
        Encoding.ASCII.GetString(file[8..16]).Should().Be("WAVEfmt ");
        BinaryPrimitives.ReadInt32LittleEndian(file[4..]).Should().Be(file.Length - 8);
        BinaryPrimitives.ReadInt16LittleEndian(file[20..]).Should().Be(1, "plain samples");
        BinaryPrimitives.ReadInt16LittleEndian(file[22..]).Should().Be(1, "one channel");
        BinaryPrimitives.ReadInt32LittleEndian(file[24..]).Should().Be(24_000);
        BinaryPrimitives.ReadInt16LittleEndian(file[34..]).Should().Be(16);
        BinaryPrimitives.ReadInt32LittleEndian(file[40..]).Should().Be(4);
        file[44..].ToArray().Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task A_recording_thrown_away_lets_the_microphone_go()
    {
        var recording = await PcmRecording.Start(capture.Listen, TestContext.Current.CancellationToken);
        capture.Hear(1, 2);

        await recording.DisposeAsync();

        capture.Listening.Should().BeFalse();
    }

    [Fact]
    public async Task A_microphone_that_cannot_be_used_fails_the_recording_as_it_fails_a_call()
    {
        capture.Refuses = AudioCaptureFailure.PermissionRefused;

        var starting = () => PcmRecording.Start(capture.Listen, TestContext.Current.CancellationToken);

        (await starting.Should().ThrowAsync<AudioCaptureException>()).Which.Failure.Should().Be(AudioCaptureFailure.PermissionRefused);
    }

    #endregion
}
