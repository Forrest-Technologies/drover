namespace Drover.Client.Tests;

/// <summary>
/// The live streams while the app is off screen. A phone app that leaves the screen closes each stream, so the server
/// no longer counts the person as watching and wakes the phone instead, and it says nothing about a dropped
/// connection. Back on screen, a stream connects again at once: a run's from the last event it handed out, the signal
/// stream saying it reconnected so the lists are read again. A stream that is through says so each time.
/// </summary>
public sealed class StreamPresenceTests
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(10);

    private static readonly Guid spaceId = Guid.Parse("0196f1a0-0000-7000-8000-000000000001");

    private static readonly Guid runId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000a1");

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_run_stream_closes_off_screen_without_a_drop_and_picks_up_from_its_last_event_once_back()
    {
        using var server = new LiveServer();
        using var presence = new StreamPresence();
        var api = new DroverApi(Http(server), presence);
        var drops = 0;
        var connects = 0;
        var seen = Channel.CreateUnbounded<long>();
        var cancellationToken = TestContext.Current.CancellationToken;
        var watching = Task.Run(async () =>
        {
            await foreach (var stored in api.WatchRun(
                spaceId,
                runId,
                dropped: _ => Interlocked.Increment(ref drops),
                connected: () => Interlocked.Increment(ref connects),
                cancellationToken: cancellationToken))
            {
                await seen.Writer.WriteAsync(stored.Seq, cancellationToken);
            }
        }, cancellationToken);

        var first = await server.Next(cancellationToken);
        await first.Send(Event(1, RunEventTypes.RunStarted));
        (await seen.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(patience, cancellationToken)).Should().Be(1);

        presence.Hide();

        await first.Closed.WaitAsync(patience, cancellationToken);
        server.LastEventIds.Should().Equal("0");

        presence.Show();

        var second = await server.Next(cancellationToken);
        server.LastEventIds.Should().Equal("0", "1");
        await second.Send(Event(2, RunEventTypes.RunCompleted));
        second.End();
        (await seen.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(patience, cancellationToken)).Should().Be(2);
        await watching.WaitAsync(patience, cancellationToken);
        Volatile.Read(ref drops).Should().Be(0, "leaving the screen is not a dropped connection");
        Volatile.Read(ref connects).Should().Be(2, "each connection says when it is through");
    }

    [Fact]
    public async Task A_dropped_run_stream_says_so_and_says_when_it_is_through_again()
    {
        using var server = new LiveServer();
        var api = new DroverApi(Http(server));
        var drops = 0;
        var connects = 0;
        var cancellationToken = TestContext.Current.CancellationToken;
        var watching = Task.Run(async () =>
        {
            await foreach (var stored in api.WatchRun(
                spaceId,
                runId,
                dropped: _ => Interlocked.Increment(ref drops),
                connected: () => Interlocked.Increment(ref connects),
                cancellationToken: cancellationToken))
            {
                stored.Seq.Should().BePositive();
            }
        }, cancellationToken);

        var first = await server.Next(cancellationToken);
        first.End();
        var second = await server.Next(cancellationToken);

        Volatile.Read(ref drops).Should().Be(1);
        (await Eventually(() => Volatile.Read(ref connects) == 2, cancellationToken)).Should().BeTrue("the second connection is through, with nothing sent on it yet");
        await second.Send(Event(1, RunEventTypes.RunCompleted));
        second.End();
        await watching.WaitAsync(patience, cancellationToken);
    }

    [Fact]
    public async Task The_signal_stream_closes_off_screen_without_a_drop_and_says_it_reconnected_once_back()
    {
        using var server = new LiveServer();
        using var presence = new StreamPresence();
        var api = new DroverApi(Http(server), presence);
        var drops = 0;
        var signals = Channel.CreateUnbounded<UserSignal>();
        var cancellationToken = TestContext.Current.CancellationToken;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watching = Task.Run(async () =>
        {
            await foreach (var signal in api.WatchSignals(spaceId, _ => Interlocked.Increment(ref drops), stop.Token))
            {
                await signals.Writer.WriteAsync(signal, cancellationToken);
            }
        }, cancellationToken);

        var first = await server.Next(cancellationToken);
        presence.Hide();
        await first.Closed.WaitAsync(patience, cancellationToken);
        presence.Show();

        (await signals.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(patience, cancellationToken)).Kind.Should().Be(UserSignalKinds.Reconnected);
        await server.Next(cancellationToken);
        Volatile.Read(ref drops).Should().Be(0);
        await stop.CancelAsync();
        await Task.WhenAny(watching);
    }

    [Fact]
    public void A_stream_that_asks_while_the_app_is_off_screen_gets_a_token_already_cancelled()
    {
        using var presence = new StreamPresence();
        var shown = presence.Hidden;

        presence.Hide();

        shown.IsCancellationRequested.Should().BeTrue();
        presence.Hidden.IsCancellationRequested.Should().BeTrue();
        presence.IsShown.Should().BeFalse();
        presence.WhenShown(TestContext.Current.CancellationToken).IsCompleted.Should().BeFalse();

        presence.Show();

        presence.Hidden.IsCancellationRequested.Should().BeFalse();
        presence.WhenShown(TestContext.Current.CancellationToken).IsCompleted.Should().BeTrue();
    }

    #endregion

    #region Private Methods

    // The banned constructor guards outbound calls in the product. This client reaches only the handler.
#pragma warning disable RS0030
    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://drover.test/") };
#pragma warning restore RS0030

    private static RunEventResponse Event(long seq, string type) => new(seq, runId, type, "{}", DateTimeOffset.UnixEpoch);

    private static async Task<bool> Eventually(Func<bool> reached, CancellationToken cancellationToken)
    {
        var until = DateTimeOffset.UtcNow + patience;

        while (!reached() && DateTimeOffset.UtcNow < until)
        {
            await Task.Delay(10, cancellationToken);
        }

        return reached();
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// A server whose every event stream is a new connection the test writes to. A run's address that is asked about
    /// is not found, so a stream that ends looks like a drop. As the real server does, the test ends a run's stream after
    /// its last event.
    /// </summary>
    private sealed class LiveServer : HttpMessageHandler
    {
        #region Private Fields

        private readonly Channel<Connection> opened = Channel.CreateUnbounded<Connection>();

        #endregion

        #region Properties

        public List<string?> LastEventIds { get; } = [];

        #endregion

        #region Public Methods

        public async Task<Connection> Next(CancellationToken cancellationToken) =>
            await opened.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(patience, cancellationToken);

        #endregion

        #region Protected Methods

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("/events", StringComparison.Ordinal)
                && !request.RequestUri.AbsolutePath.EndsWith("/stream", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            lock (LastEventIds)
            {
                LastEventIds.Add(request.Headers.TryGetValues("Last-Event-ID", out var ids) ? ids.Single() : null);
            }

            var connection = new Connection();
            opened.Writer.TryWrite(connection);
            var content = new StreamContent(connection.Body);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        #endregion
    }

    /// <summary>One open stream: what the test sends on it, and whether the client has closed it.</summary>
    private sealed class Connection
    {
        #region Private Fields

        private readonly Pipe pipe = new();

        private readonly TaskCompletionSource closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Constructors

        public Connection() => Body = new ClosingStream(pipe.Reader.AsStream(), () => closed.TrySetResult());

        #endregion

        #region Properties

        public Stream Body { get; }

        public Task Closed => closed.Task;

        #endregion

        #region Public Methods

        public async Task Send(RunEventResponse stored)
        {
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes($"id: {stored.Seq}\ndata: {JsonSerializer.Serialize(stored, JsonSerializerOptions.Web)}\n\n"));
            await pipe.Writer.FlushAsync();
        }

        public void End() => pipe.Writer.Complete();

        #endregion
    }

    /// <summary>A body that says when the client lets go of it, which is when its connection closes.</summary>
    private sealed class ClosingStream(Stream inner, Action closing) : Stream
    {
        #region Properties

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        #endregion

        #region Public Methods

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        #endregion

        #region Protected Methods

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closing();
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        #endregion
    }

    #endregion
}
