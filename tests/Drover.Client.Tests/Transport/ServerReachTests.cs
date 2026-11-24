namespace Drover.Client.Tests.Transport;

public sealed class ServerReachTests
{
    #region Private Fields

    private static readonly Uri origin = new("https://drover.example.test/");

    private static readonly TimeSpan[] noWait = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_refused_connection_is_tried_again_and_the_server_that_then_answers_is_found()
    {
        var transport = new Server(Refused, Refused, Answers);
        var logger = new Recorder();

        var found = await Open(transport).IsDroverServer(origin, logger, TestContext.Current.CancellationToken);

        found.Should().BeTrue();
        transport.Requests.Should().Be(3);
        logger.Warnings.Should().Be(2);
    }

    [Fact]
    public async Task A_server_that_stays_down_fails_once_the_backoff_is_spent_with_the_last_refusal()
    {
        var transport = new Server(Refused, Refused, Refused, Refused, Answers);
        var logger = new Recorder();

        var reach = () => Open(transport).IsDroverServer(origin, logger, TestContext.Current.CancellationToken);

        var failure = await reach.Should().ThrowAsync<HttpRequestException>();
        failure.Which.HttpRequestError.Should().Be(HttpRequestError.ConnectionError);
        transport.Requests.Should().Be(noWait.Length + 1);
        logger.Warnings.Should().Be(noWait.Length);
    }

    [Fact]
    public async Task An_address_that_is_not_a_drover_server_is_not_tried_again()
    {
        var transport = new Server(NotDrover, Answers);
        var logger = new Recorder();

        var reach = () => Open(transport).IsDroverServer(origin, logger, TestContext.Current.CancellationToken);

        await reach.Should().ThrowAsync<HttpRequestException>();
        transport.Requests.Should().Be(1);
        logger.Warnings.Should().Be(0);
    }

    [Fact]
    public async Task Retries_stop_when_the_caller_gives_up()
    {
        using var cancelled = new CancellationTokenSource();
        var transport = new Server(Refused, Refused);
        var logger = new Recorder { OnWarning = cancelled.Cancel };

        var reach = () => Open(transport).IsDroverServer(origin, logger, cancelled.Token);

        await reach.Should().ThrowAsync<OperationCanceledException>();
        transport.Requests.Should().Be(1);
    }

    #endregion

    #region Private Methods

    private static AppRequests Open(Server transport)
    {
        var address = new ServerAddress();
        address.Set(origin);
        var session = new NativeSession(new NoStorage(), TimeProvider.System, NullLogger<NativeSession>.Instance);
        return new AppRequests(address, session, transport, noWait);
    }

    private static HttpResponseMessage Refused(HttpClientHandler handler) =>
        throw new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused");

    private static HttpResponseMessage NotDrover(HttpClientHandler handler) => new(HttpStatusCode.NotFound);

    private static HttpResponseMessage Answers(HttpClientHandler handler)
    {
        handler.CookieContainer.Add(origin, new Cookie("XSRF-TOKEN", "token", "/"));
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    #endregion

    #region Types

    private sealed class Server(params Func<HttpClientHandler, HttpResponseMessage>[] answers) : HttpClientHandler
    {
        #region Private Fields

        private readonly Queue<Func<HttpClientHandler, HttpResponseMessage>> remaining = new(answers);

        #endregion

        #region Properties

        public int Requests { get; private set; }

        #endregion

        #region Interface Implementations

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(remaining.Dequeue()(this));
        }

        #endregion
    }

    private sealed class Recorder : ILogger<AppRequests>
    {
        #region Properties

        public int Warnings { get; private set; }

        public Action? OnWarning { get; init; }

        #endregion

        #region Interface Implementations

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings++;
                OnWarning?.Invoke();
            }
        }

        #endregion
    }

    private sealed class NoStorage : ISecureStorage
    {
        #region Interface Implementations

#pragma warning disable DRV0001 // Matches the storage API used by the linked transport.
        public Task<string?> GetAsync(string key) => Task.FromResult<string?>(null);

        public Task SetAsync(string key, string value) => Task.CompletedTask;
#pragma warning restore DRV0001

        public bool Remove(string key) => true;

        #endregion
    }

    #endregion
}
