namespace Drover.Native.Desktop.Tests;

/// <summary>
/// The desktop's sound against the system's real sound service: the library's native part loads on this system, what
/// is queued is played and counted, a flush stops it, and the microphone hands over whole 16-bit samples. It needs an
/// output and a microphone, which a build machine has none of, so it is explicit and marked Live: the normal run and
/// pull request CI never execute it. On Linux a sound server with a null output stands in for both:
/// <c>pulseaudio -D --exit-idle-time=-1 &amp;&amp; pactl load-module module-null-sink</c>, then
/// <c>dotnet test --project tests/Drover.Native.Desktop.Tests -- --explicit only --filter-trait "Live=true"</c>.
/// Only silence is played.
/// </summary>
[Trait("Live", "true")]
public sealed class DesktopAudioTests : IDisposable
{
    #region Private Fields

    private const int Rate = 24_000;

    private readonly DesktopAudio audio = new(NullLogger<DesktopAudio>.Instance);

    #endregion

    #region Public Methods

    public void Dispose() => audio.Dispose();

    [Fact(Explicit = true)]
    public async Task What_is_queued_is_played_to_its_end_and_counted()
    {
        await using var output = await audio.Open(Rate, TestContext.Current.CancellationToken);
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.Drained += () => drained.TrySetResult();

        output.Queue(Silence(milliseconds: 200));
        output.IsPlaying.Should().BeTrue();
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        output.IsPlaying.Should().BeFalse();
        output.PlayedMilliseconds.Should().Be(200);
    }

    [Fact(Explicit = true)]
    public async Task A_flush_drops_what_was_queued_and_keeps_the_count_of_what_was_played()
    {
        await using var output = await audio.Open(Rate, TestContext.Current.CancellationToken);

        output.Queue(Silence(milliseconds: 10_000));
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        output.Flush();
        var played = output.PlayedMilliseconds;

        output.IsPlaying.Should().BeFalse();
        played.Should().BeLessThan(10_000);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        output.PlayedMilliseconds.Should().Be(played, "nothing is played after a flush");
    }

    [Fact(Explicit = true)]
    public async Task The_microphone_hands_over_whole_samples_and_a_recording_is_a_wav_file()
    {
        var heard = 0;

        // Something plays meanwhile, as on a call: a sound server that stands in for the devices is asleep until then.
        await using var output = await audio.Open(Rate, TestContext.Current.CancellationToken);
        output.Queue(Silence(milliseconds: 20_000));

        await using (await audio.Listen(samples => Interlocked.Add(ref heard, samples.Length), TestContext.Current.CancellationToken))
        {
            await Until(() => Volatile.Read(ref heard) > Rate);
        }

        (heard % PcmAudio.BytesPerSample).Should().Be(0);

        var recording = await audio.Record(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);
        var clip = await recording.Finish(TestContext.Current.CancellationToken);
        await recording.DisposeAsync();

        clip.MediaType.Should().Be("audio/wav");
        clip.Audio.Span[..4].SequenceEqual("RIFF"u8).Should().BeTrue();
        BinaryPrimitives.ReadInt32LittleEndian(clip.Audio.Span[24..]).Should().Be(Rate);
        clip.Audio.Length.Should().BeGreaterThan(44);
    }

    #endregion

    #region Private Methods

    // A device takes a moment to start handing over what it hears.
    private static async Task Until(Func<bool> condition)
    {
        for (var tries = 0; tries < 200 && !condition(); tries++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }

        condition().Should().BeTrue("the microphone should have handed over half a second of sound within ten seconds");
    }

    private static byte[] Silence(int milliseconds) => new byte[Rate * PcmAudio.BytesPerSample * milliseconds / 1000];

    #endregion
}
