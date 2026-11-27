namespace Drover.Server.Tests.Support;

/// <summary>
/// Stands in for a speech provider behind a compatible connection: it records what it was asked and answers from
/// what the test set. Nothing leaves the process.
/// </summary>
public sealed class FakeSpeech : ISpeechToText, ITextToSpeech, IRealtimeVoice
{
    #region Properties

    public string Kind { get; init; } = ModelConnectionKinds.Compatible;

    public string TranscriptText { get; set; } = "hello from the recording";

    public ModelProviderException? Failure { get; set; }

    public IReadOnlyList<VoiceOption> Voices { get; set; } = [new("alloy", "Alloy"), new("verse", "Verse")];

    public ConcurrentQueue<(Uri Address, string? Key, string Model, string MediaType, int Bytes)> Transcriptions { get; } = [];

    public ConcurrentQueue<(Uri Address, string? Key, SpeechRequest Request)> Spoken { get; } = [];

    /// <summary>When set, the live model only holds the conversation and hands its work over.</summary>
    public bool HandsOver { get; set; }

    public ConcurrentQueue<FakeCall> Calls { get; } = [];

    #endregion

    #region Interface Implementations

    public async Task<Transcript> Transcribe(ModelEndpoint connection, string? apiKey, TranscriptionRequest request, CancellationToken cancellationToken)
    {
        using var held = new MemoryStream();
        await request.Audio.CopyToAsync(held, cancellationToken);
        Transcriptions.Enqueue((connection.BaseAddress, apiKey, request.Model, request.MediaType, (int)held.Length));
        return Failure is null ? new Transcript(TranscriptText) { Seconds = 1.5 } : throw Failure;
    }

    public async IAsyncEnumerable<ReadOnlyMemory<byte>> Speak(
        ModelEndpoint connection,
        string? apiKey,
        SpeechRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Spoken.Enqueue((connection.BaseAddress, apiKey, request));
        await Task.Yield();

        if (Failure is not null)
        {
            throw Failure;
        }

        yield return Encoding.UTF8.GetBytes($"[{request.Format.WireName()}:{request.Voice}:");
        yield return Encoding.UTF8.GetBytes($"{request.Text}]");
    }

    public Task<IReadOnlyList<VoiceOption>> ListVoices(ModelEndpoint connection, string? apiKey, string model, CancellationToken cancellationToken) =>
        Failure is null ? Task.FromResult(Voices) : throw Failure;

    public IReadOnlyList<VoiceOption> ListVoices(string model) => Voices;

    public bool HandsWorkOver(string model) => HandsOver;

    public Task<IRealtimeVoiceSession> Open(ModelEndpoint connection, string? apiKey, RealtimeVoiceOptions options, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        var call = new FakeCall(connection.BaseAddress, apiKey, options);
        Calls.Enqueue(call);
        return Task.FromResult<IRealtimeVoiceSession>(call);
    }

    #endregion
}


/// <summary>One open call with the stand-in: the test says what the model does, and reads what the person sent.</summary>
public sealed class FakeCall(Uri address, string? key, RealtimeVoiceOptions options) : IRealtimeVoiceSession
{
    #region Private Fields

    private readonly Channel<RealtimeVoiceEvent> events = Channel.CreateUnbounded<RealtimeVoiceEvent>();

    private readonly Channel<byte[]> heard = Channel.CreateUnbounded<byte[]>();

    private readonly Channel<int> interrupts = Channel.CreateUnbounded<int>();

    private readonly Channel<(string WorkId, string Text)> reports = Channel.CreateUnbounded<(string WorkId, string Text)>();

    #endregion

    #region Properties

    public Uri Address => address;

    public string? Key => key;

    public RealtimeVoiceOptions Options => options;

    public bool HungUp { get; private set; }

    public ChannelReader<byte[]> Heard => heard.Reader;

    public ChannelReader<int> Interrupts => interrupts.Reader;

    /// <summary>What the model was told its handed-over work came to, in order.</summary>
    public ChannelReader<(string WorkId, string Text)> Reports => reports.Reader;

    #endregion

    #region Public Methods

    public void Does(params RealtimeVoiceEvent[] happened)
    {
        foreach (var one in happened)
        {
            events.Writer.TryWrite(one);
        }
    }

    public void Ends() => events.Writer.TryComplete();

    #endregion

    #region Interface Implementations

    public ValueTask SendAudio(ReadOnlyMemory<byte> samples, CancellationToken cancellationToken) =>
        heard.Writer.WriteAsync(samples.ToArray(), cancellationToken);

    public ValueTask Interrupt(int heardMilliseconds, CancellationToken cancellationToken) =>
        interrupts.Writer.WriteAsync(heardMilliseconds, cancellationToken);

    public ValueTask Report(string workId, string text, CancellationToken cancellationToken) =>
        reports.Writer.WriteAsync((workId, text), cancellationToken);

    public IAsyncEnumerable<RealtimeVoiceEvent> Read(CancellationToken cancellationToken) => events.Reader.ReadAllAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        HungUp = true;
        return ValueTask.CompletedTask;
    }

    #endregion
}
