namespace Drover.Client.Tests;

/// <summary>
/// What the app does with a notification wake-up after the distributor has handed it over, against a server that
/// answers, fails, or never answers: the person is told something changed whenever either request fails or runs out of
/// time, and is shown nothing when the session is another person's or the registration is no longer this one.
/// </summary>
public sealed class PushWakeFlowTests
{
    #region Private Fields

    private static readonly Guid user = Guid.Parse("8f0b6f5e-3d7d-4a55-9f6b-2f3d6a1b0c11");

    private static readonly Guid thread = Guid.Parse("2d1d4c3b-6a77-4f0e-8c1a-0b9a5c4d3e22");

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_wake_whose_session_request_fails_still_tells_the_person_something_changed()
    {
        var server = new Server { OnSession = _ => throw new HttpRequestException("The network is not up yet.") };

        var shown = await Run(server, "run.finished");

        shown.Notices.Should().Equal("change");
        shown.Log.Should().ContainSingle().Which.Should().Contain("HttpRequestException").And.NotContain("network");
    }

    [Fact]
    public async Task A_wake_whose_session_request_runs_out_of_time_still_tells_the_person_something_changed()
    {
        using var patience = new CancellationTokenSource();
        var server = new Server
        {
            OnSession = async token =>
            {
                await patience.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return null;
            },
        };

        var shown = await Run(server, "approval.waiting", patience);

        shown.Notices.Should().Equal("change");
        shown.Log.Should().ContainSingle().Which.Should().Contain("TaskCanceledException");
    }

    [Fact]
    public async Task A_wake_whose_session_is_refused_by_the_server_still_tells_the_person_something_changed()
    {
        var server = new Server { OnSession = _ => throw new DroverApiException(HttpStatusCode.BadGateway, "Down.") };

        var shown = await Run(server, "run.failed");

        shown.Notices.Should().Equal("change");
    }

    [Fact]
    public async Task A_wake_whose_words_cannot_be_read_still_tells_the_person_something_changed()
    {
        var server = new Server { OnWake = _ => throw new HttpRequestException("Reset.") };

        var shown = await Run(server, "run.finished");

        shown.Notices.Should().Equal("change");
        shown.Log.Should().ContainSingle().Which.Should().Contain("HttpRequestException");
    }

    [Fact]
    public async Task A_wake_whose_words_run_out_of_time_still_tells_the_person_something_changed()
    {
        using var patience = new CancellationTokenSource();
        var server = new Server
        {
            OnWake = async token =>
            {
                await patience.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return null;
            },
        };

        var shown = await Run(server, "run.finished", patience);

        shown.Notices.Should().Equal("change");
    }

    [Fact]
    public async Task A_failure_after_the_registration_was_replaced_shows_nothing()
    {
        var server = new Server { OnSession = _ => throw new HttpRequestException("Down.") };

        var shown = await Run(server, "run.finished", belongs: false);

        shown.Notices.Should().BeEmpty();
    }

    [Fact]
    public async Task A_wake_that_works_shows_the_words_the_server_gave_and_offers_a_reply_for_a_finished_run()
    {
        var shown = await Run(new Server(), "run.finished");
        var waiting = await Run(new Server(), "approval.waiting");

        shown.Notices.Should().Equal("words, reply");
        waiting.Notices.Should().Equal("words");
        shown.Log.Should().BeEmpty();
    }

    [Fact]
    public async Task A_test_wake_shows_the_test_notice_and_asks_for_no_words()
    {
        var server = new Server { OnWake = _ => throw new InvalidOperationException("No words are asked for a test.") };

        var shown = await Run(server, "device.test", thread: Guid.Empty);

        shown.Notices.Should().Equal("test");
    }

    [Fact]
    public async Task A_session_that_answers_with_another_person_or_none_shows_nothing()
    {
        var another = await Run(new Server { Person = Guid.NewGuid() }, "run.finished");
        var signedOut = await Run(new Server { SignedOut = true }, "run.finished");

        another.Notices.Should().BeEmpty();
        signedOut.Notices.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"kind":"run.finished"}""")]
    [InlineData("""{"kind":"something.else","thread":"2d1d4c3b-6a77-4f0e-8c1a-0b9a5c4d3e22"}""")]
    [InlineData("""{"kind":"run.finished","thread":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("[]")]
    public async Task A_wake_that_is_not_one_of_the_known_kinds_shows_nothing_and_asks_the_server_nothing(string clear)
    {
        var server = new Server { OnSession = _ => throw new InvalidOperationException("Nothing is asked.") };

        var shown = await Run(server, clear: Encoding.UTF8.GetBytes(clear));

        shown.Notices.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static async Task<Shown> Run(
        Server server,
        string? kind = null,
        CancellationTokenSource? patience = null,
        bool belongs = true,
        Guid? thread = null,
        byte[]? clear = null)
    {
        var notices = new Notices();
        var log = new Recorder();
        var saved = AndroidPushSubscription.Create("org.sample.distributor", user);
        var about = thread ?? PushWakeFlowTests.thread;
        var payload = clear ?? Encoding.UTF8.GetBytes($$"""{"kind":"{{kind}}","thread":"{{about}}"}""");

        // The banned constructor guards outbound calls in the product. This client reaches only the scripted server.
#pragma warning disable RS0030
        using var http = new HttpClient(server) { BaseAddress = new Uri("https://drover.test/") };
#pragma warning restore RS0030
        var flow = new PushWakeFlow(new DroverApi(http), log);

        var token = patience?.Token ?? TestContext.Current.CancellationToken;
        await flow.Show(saved, payload, () => Task.FromResult(belongs), notices, token);

        return new(notices.Seen, log.Lines);
    }

    #endregion

    #region Nested Types

    private sealed record Shown(IReadOnlyList<string> Notices, IReadOnlyList<string> Log);

    private sealed class Notices : IPushNotices
    {
        #region Properties

        public List<string> Seen { get; } = [];

        #endregion

        #region Interface Implementations

        public void Show(PushWakeText wake, AndroidPushSubscription saved, bool canReply) =>
            Seen.Add(canReply ? "words, reply" : "words");

        public void ShowChange() => Seen.Add("change");

        public void ShowTest() => Seen.Add("test");

        #endregion
    }

    /// <summary>A logger that keeps each line as the type of any exception it was given and the message.</summary>
    private sealed class Recorder : ILogger
    {
        #region Properties

        public List<string> Lines { get; } = [];

        #endregion

        #region Interface Implementations

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add($"{exception?.GetType().Name} {formatter(state, exception)}".Trim());

        #endregion
    }

    private sealed class Server : HttpMessageHandler
    {
        #region Properties

        public Guid Person { get; init; } = user;

        public bool SignedOut { get; init; }

        public Func<CancellationToken, Task<HttpResponseMessage?>>? OnSession { get; init; }

        public Func<CancellationToken, Task<PushWakeText?>>? OnWake { get; init; }

        #endregion

        #region Protected Methods

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/session", StringComparison.Ordinal))
            {
                return OnSession is { } scripted ? await scripted(cancellationToken) ?? Session() : Session();
            }

            if (OnWake is not null)
            {
                return Json(await OnWake(cancellationToken));
            }

            return Json(new PushWakeText("The bot finished.", Guid.NewGuid(), thread, IsMain: true));
        }

        #endregion

        #region Private Methods

        private HttpResponseMessage Session() => SignedOut
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : Json(new SessionResponse(Person, "person@example.test", "Person", []));

        private static HttpResponseMessage Json<T>(T? value) => value is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage { Content = JsonContent.Create(value) };

        #endregion
    }

    #endregion
}
