namespace Drover.Native.Pages;

/// <summary>
/// The gallery's stand-in for a server. The panels beside a conversation read what they show from the server, so the
/// gallery answers their requests itself, with samples, and nothing leaves the app. Every name and value it answers
/// with is made up, and the gallery says so at its top.
/// </summary>
internal sealed class SampleServer : HttpMessageHandler
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private static readonly Uri origin = new("https://sample.invalid/");

    private readonly Dictionary<string, Func<HttpContent?>> answers = [];

    private readonly Dictionary<string, HttpStatusCode> refusals = [];

    #endregion

    #region Properties

    /// <summary>The sample space every sample address is in.</summary>
    public static Guid SpaceId { get; } = Guid.Parse("00000000-0000-7000-8000-0000000000f0");

    public static string Space => $"/api/spaces/{SpaceId}";

    #endregion

    #region Public Methods

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The gallery serializes fixed sample contract types from the rooted Drover.Contracts assembly.")]
    public SampleServer Answer<T>(string method, string path, T body)
    {
        answers[$"{method} {path}"] = () => new StringContent(JsonSerializer.Serialize(body, json), System.Text.Encoding.UTF8, "application/json");
        return this;
    }

    public SampleServer AnswerBytes(string path, byte[] body, string contentType)
    {
        answers[$"GET {path}"] = () => new ByteArrayContent(body) { Headers = { ContentType = new(contentType) } };
        return this;
    }

    /// <summary>Refuses, as the server does when the person may not have what they asked for.</summary>
    public SampleServer Refuse(string method, string path, HttpStatusCode status)
    {
        refusals[$"{method} {path}"] = status;
        return this;
    }

    /// <summary>Never answers, so a panel can be seen while it waits for the server.</summary>
    public SampleServer Hold(string method, string path)
    {
        answers[$"{method} {path}"] = () => null;
        return this;
    }

    /// <summary>
    /// Answers with a run's events as a stream that stays open after the last one, as the stream of a run that is
    /// still going does.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "The gallery serializes fixed sample contract types from the rooted Drover.Contracts assembly.")]
    public SampleServer Stream(string path, IEnumerable<RunEventResponse> events)
    {
        var sent = System.Text.Encoding.UTF8.GetBytes(string.Concat(events.Select(stored => $"data: {JsonSerializer.Serialize(stored, json)}\n\n")));
        answers[$"GET {path}"] = () => new StreamContent(new OpenEnded(sent)) { Headers = { ContentType = new("text/event-stream") } };
        return this;
    }

    /// <summary>A client whose every request is answered here, and a session signed in to the sample space.</summary>
    public (DroverApi Api, SessionState Session) Connect(string role = "owner")
    {
        // The one client the gallery builds itself: it reaches no network, only the samples above.
#pragma warning disable RS0030
        var api = new DroverApi(new HttpClient(this, disposeHandler: false) { BaseAddress = origin });
#pragma warning restore RS0030
        var session = new SessionState(api, new ShareInbox());
        session.Begin(new(
            Guid.Parse("00000000-0000-7000-8000-0000000000f1"),
            "sample@example.test",
            "Sample person",
            [new(SpaceId, "personal", "Sample space", "sample", role)]));
        return (api, session);
    }

    #endregion

    #region Private Methods

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var asked = $"{request.Method.Method} {request.RequestUri!.AbsolutePath}";
        if (refusals.TryGetValue(asked, out var refusal))
        {
            return new(refusal);
        }

        if (!answers.TryGetValue(asked, out var answer))
        {
            return new(HttpStatusCode.InternalServerError);
        }

        if (answer() is not { } content)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(HttpStatusCode.RequestTimeout);
        }

        return new(HttpStatusCode.OK) { Content = content };
    }

    #endregion

    #region Nested Types

    // What was sent, then nothing more for as long as the reader stays.
    private sealed class OpenEnded(byte[] sent) : Stream
    {
        #region Private Fields

        private int position;

        #endregion

        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        #endregion

        #region Public Methods

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (position >= sent.Length)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var count = Math.Min(buffer.Length, sent.Length - position);
            sent.AsMemory(position, count).CopyTo(buffer);
            position += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("The sample stream is read as it arrives.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion
    }

    #endregion
}
