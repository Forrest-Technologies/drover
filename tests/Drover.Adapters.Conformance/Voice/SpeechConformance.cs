namespace Drover.Adapters.Conformance.Voice;

/// <summary>
/// What every speech adapter must do, whatever its wire: write a recording down, stream read-aloud audio, list
/// voices, and report failures in Drover's own terms without the provider's text.
/// </summary>
public abstract class SpeechConformance
{
    #region Adapter Hooks

    /// <summary>An adapter whose provider answers normally: the transcript, the audio and the voices given here.</summary>
    protected abstract SpeechUnderTest Start(string transcript, byte[] audio, IReadOnlyList<string> voices);

    /// <summary>An adapter whose provider answers every request with this status and a body holding <paramref name="providerText"/>.</summary>
    protected abstract SpeechUnderTest StartFailing(int status, string providerText);

    #endregion

    #region Transcription

    [Fact]
    public async Task Writes_down_a_recording()
    {
        var subject = Start("Remind me at nine.", [1, 2, 3], []);
        using var audio = new MemoryStream([82, 73, 70, 70, 1, 2, 3, 4]);

        var transcript = await subject.Listener.Transcribe(
            subject.Connection,
            subject.ApiKey,
            new TranscriptionRequest(audio, "audio/wav", subject.Model, 1024),
            TestContext.Current.CancellationToken);

        transcript.Text.Should().Be("Remind me at nine.");
    }

    [Fact]
    public async Task Refuses_a_recording_past_the_bound_before_asking_the_provider()
    {
        var subject = Start("never read", [1], []);
        using var audio = new MemoryStream(new byte[2048]);

        var call = async () => await subject.Listener.Transcribe(
            subject.Connection,
            subject.ApiKey,
            new TranscriptionRequest(audio, "audio/wav", subject.Model, 1024),
            TestContext.Current.CancellationToken);

        (await call.Should().ThrowAsync<ModelProviderException>()).Which.Failure.Should().Be(ModelFailure.Rejected);
        subject.RequestCount().Should().Be(0);
    }

    #endregion

    #region Read Aloud

    [Fact]
    public async Task Streams_every_byte_of_the_audio_in_order()
    {
        var expected = Enumerable.Range(0, 70_000).Select(index => (byte)(index % 251)).ToArray();
        var subject = Start("unused", expected, []);
        List<byte> heard = [];

        await foreach (var chunk in subject.Speaker.Speak(
            subject.Connection,
            subject.ApiKey,
            new SpeechRequest("Good morning.", subject.Model, "af_heart", SpeechFormat.Mp3),
            TestContext.Current.CancellationToken))
        {
            chunk.Length.Should().BeGreaterThan(0);
            heard.AddRange(chunk.Span);
        }

        heard.Should().Equal(expected);
    }

    [Fact]
    public async Task Lists_the_voices_the_provider_names()
    {
        var subject = Start("unused", [1], ["af_heart", "am_michael"]);

        var voices = await subject.Speaker.ListVoices(subject.Connection, subject.ApiKey, subject.Model, TestContext.Current.CancellationToken);

        voices.Select(voice => voice.Id).Should().Equal("af_heart", "am_michael");
    }

    #endregion

    #region Failures

    [Theory]
    [InlineData(401, ModelFailure.InvalidKey)]
    [InlineData(429, ModelFailure.RetryLater)]
    [InlineData(402, ModelFailure.OutOfCredit)]
    [InlineData(500, ModelFailure.Unreachable)]
    [InlineData(400, ModelFailure.Rejected)]
    public async Task Maps_provider_failures_and_never_repeats_the_providers_text(int status, ModelFailure expected)
    {
        const string ProviderText = "PROVIDER-SAID-THIS-7731";
        var subject = StartFailing(status, ProviderText);
        using var audio = new MemoryStream([1, 2, 3]);

        var listening = async () => await subject.Listener.Transcribe(
            subject.Connection,
            subject.ApiKey,
            new TranscriptionRequest(audio, "audio/wav", subject.Model, 1024),
            TestContext.Current.CancellationToken);
        var speaking = async () =>
        {
            await foreach (var chunk in subject.Speaker.Speak(
                subject.Connection,
                subject.ApiKey,
                new SpeechRequest("Hello.", subject.Model, "af_heart", SpeechFormat.Mp3),
                TestContext.Current.CancellationToken))
            {
                chunk.Length.Should().BeGreaterThan(0);
            }
        };

        foreach (var call in new[] { listening, speaking })
        {
            var failure = (await call.Should().ThrowAsync<ModelProviderException>()).Which;
            failure.Failure.Should().Be(expected);
            failure.Message.Should().NotContain(ProviderText);
            failure.ToString().Should().NotContain(subject.ApiKey);
        }
    }

    #endregion
}


/// <summary>A speech adapter wired to a stand-in provider.</summary>
public sealed record SpeechUnderTest(
    ISpeechToText Listener,
    ITextToSpeech Speaker,
    ModelEndpoint Connection,
    string ApiKey,
    string Model,
    Func<int> RequestCount);
