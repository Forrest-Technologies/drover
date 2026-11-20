namespace Drover.Ui.Tests.Web;

/// <summary>
/// The published service worker in a real Chromium, served by a small server in this test that sends the worker the
/// same content security policy Drover's server does. Requests to another host, the server's API and sign-in
/// callbacks, and anything that is not a GET go to the network without the worker; a request it cannot answer leaves
/// no unhandled rejection; a page load the server cannot answer gets the stored shell. A push wakes the worker, which
/// asks this server with the person's own cookie what to show, and tapping it opens that conversation. The browser is
/// DROVER_TEST_CHROMIUM, or the first Chromium or Chrome at a usual path. The same worker serves the web client at the
/// root and the native client's browser build under /native/, so every case runs for both (<see cref="ServiceWorkerTests"/>,
/// <see cref="NativeServiceWorkerTests"/>).
/// </summary>
public abstract class ServiceWorkerScenarios : IAsyncLifetime
{
    #region Private Fields

    /// <summary>The policy Drover's server sends with every file (SecurityHeaders), cut to the parts a worker uses.</summary>
    private const string WorkerPolicy = "default-src 'self'; connect-src 'self'";

    /// <summary>Put before the worker: it keeps every unhandled rejection and hands them to the page that asks.</summary>
    private const string RejectionRecorder =
        "self.rejections = []; self.addEventListener('unhandledrejection', e => self.rejections.push(String(e.reason))); "
        + "self.addEventListener('message', e => e.source.postMessage(self.rejections));\n";

    /// <summary>Put before the worker: a page asks it to open a conversation, as tapping a notification does.</summary>
    private const string OpenRecorder =
        "self.addEventListener('message', e => { if (e.data && e.data.open) { openConversation({ url: e.data.open }); } });\n";

    // A synthetic PushEvent has no browser-managed lifetime. Keep the worker alive through the message event and
    // report whether the production push handler completed the work it registered.
    private const string PushDispatcher = """
        self.addEventListener('message', message => {
            if (typeof message.data?.testPush !== 'string') return;
            const reply = message.ports[0];
            const event = new PushEvent('push', { data: message.data.testPush });
            const work = [];
            event.waitUntil = promise => work.push(promise);
            self.dispatchEvent(event);
            if (work.length === 0) {
                reply.postMessage('The push handler registered no work.');
                return;
            }
            message.waitUntil(Promise.all(work).then(
                () => reply.postMessage(null), error => reply.postMessage(String(error))));
        });
        """ + "\n";

    // The headless browser has no push service, so it has no subscription. This gives the worker one the test sets,
    // and counts how many times the worker ends it.
    private const string SubscriptionStub = """
        self.testSubscription = 'https://push.example/test-subscription';
        self.unsubscribeCount = 0;
        PushManager.prototype.getSubscription = async function () {
            const endpoint = self.testSubscription;
            return endpoint === null ? null : {
                endpoint,
                unsubscribe: async () => { self.unsubscribeCount++; self.testSubscription = null; return true; },
            };
        };
        self.addEventListener('message', message => {
            if (message.data === null || typeof message.data !== 'object' || !('subscription' in message.data || message.data.unsubscribeCount)) return;
            if ('subscription' in message.data) self.testSubscription = message.data.subscription;
            message.ports[0].postMessage(self.unsubscribeCount);
        });
        """ + "\n";

    private const string TestSubscription = "https://push.example/test-subscription";

    private const string AnotherPersonsSubscription = "https://push.example/another-persons-subscription";

    /// <summary>The person the test server knows from the sign-in cookie, and a different one a wake can be sent for.</summary>
    private static readonly Guid signedInPerson = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d1");

    private static readonly Guid anotherPerson = Guid.Parse("0196f1a0-0000-7000-8000-0000000000d2");

    /// <summary>The person's sign-in, as a cookie the worker's own request to the server must carry.</summary>
    private const string SignInCookie = "drover-test-session=signed-in";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000b1");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

    /// <summary>The shell the worker stores at install, as Drover's index.html is, and serves when a page cannot load.</summary>
    private string Shell => $"<!DOCTYPE html><title>Worker test shell</title><script src=\"{Home}register.js\"></script>";

    private static readonly string[] browserPaths = ["/opt/pw-browsers/chromium", "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"];

    private readonly List<IResponse> responses = [];

    private WebApplication? other;

    private WebApplication? shell;

    private IPlaywright? playwright;

    private IBrowser? browser;

    private IPage page = null!;

    private readonly List<string> wakeQueries = [];

    #endregion

    #region Properties

    private string Other => other!.Urls.Single();

    protected string Origin => shell!.Urls.Single();

    /// <summary>The folder the client is served from, and the worker's scope: "/" or "/native/".</summary>
    protected abstract string Home { get; }

    protected IPage Page => page;

    protected List<IResponse> Responses => responses;

    #endregion

    #region Public Methods

    [Theory]
    [InlineData("another host", "GET", false)]
    [InlineData("/api/data", "GET", false)]
    [InlineData("/api/data", "POST", false)]
    [InlineData("/oauth/client.json", "GET", false)]
    [InlineData("register.js", "GET", true)]
    public async Task Only_drovers_own_files_and_pages_are_answered_by_the_worker(string address, string method, bool byWorker)
    {
        var url = address switch
        {
            "another host" => $"{Other}/data",
            ['/', ..] => $"{Origin}{address}",
            _ => $"{Origin}{Home}{address}",
        };

        (await Fetch(url, method)).Should().Be("200", "the request reaches its server, with or without the worker");

        responses.Should().Contain(response => response.Url == url && response.Request.Method == method && response.FromServiceWorker == byWorker);
    }

    [Fact]
    public async Task A_request_the_worker_cannot_answer_fails_without_an_unhandled_rejection()
    {
        (await Fetch($"{Origin}{Home}broken.js", "GET")).Should().StartWith("failed");

        await Task.Delay(TimeSpan.FromMilliseconds(300), Xunit.TestContext.Current.CancellationToken);
        var rejections = await page.EvaluateAsync<string[]>(
            "() => new Promise(done => { navigator.serviceWorker.addEventListener('message', e => done(e.data), { once: true }); navigator.serviceWorker.controller.postMessage('rejections'); })");
        rejections.Should().BeEmpty();
    }

    [Fact]
    public async Task A_page_the_server_cannot_answer_gets_the_stored_shell_whether_or_not_it_is_a_navigation()
    {
        var asked = await page.EvaluateAsync<string>(
            "async url => { const answer = await fetch(url, { headers: { Accept: 'text/html' } }); return `${answer.status} ${await answer.text()}`; }",
            $"{Origin}{Home}bots/unreachable");
        asked.Should().Be($"200 {Shell}", "a GET for a page address that is not a navigation is still a page load");

        await page.GotoAsync($"{Origin}{Home}bots/unreachable");
        (await page.TitleAsync()).Should().Be("Worker test shell");
    }

    [Fact]
    public async Task A_push_shows_what_the_server_says_asked_for_with_the_persons_own_sign_in()
    {
        await DeliverPush($$"""{"kind":"run.finished","thread":"{{threadId}}"}""");

        var shown = await WaitForNotifications();

        shown.Should().Equal($"Drover|Sample bot finished.|{Home}talk/{botId}");
        wakeQueries.Should().Equal($"?kind=run.finished&thread={threadId}|{SignInCookie}|{TestSubscription}");
    }

    [Fact]
    public async Task A_push_the_server_does_not_explain_still_shows_without_any_of_the_conversation()
    {
        await DeliverPush($$"""{"kind":"run.finished","thread":"{{Guid.Empty}}"}""");

        var shown = await WaitForNotifications();

        shown.Should().Equal($"Drover|Something changed. Open Drover to see it.|{Home}");
        wakeQueries.Should().Equal($"?kind=run.finished&thread={Guid.Empty}|{SignInCookie}|{TestSubscription}");
    }

    [Fact]
    public async Task A_push_left_for_the_last_person_shows_nothing_and_ends_the_browsers_subscription()
    {
        await SetSubscription(AnotherPersonsSubscription);

        await DeliverPush($$"""{"kind":"run.finished","thread":"{{threadId}}","user":"{{signedInPerson}}"}""");

        (await ShownNotifications()).Should().BeEmpty("the signed-in person holds no subscription for this address, so nothing is shown");
        wakeQueries.Should().Equal($"?kind=run.finished&thread={threadId}&user={signedInPerson}|{SignInCookie}|{AnotherPersonsSubscription}");
        (await SetSubscription(null)).Should().Be(1, "the worker ends a subscription the signed-in person does not hold");
    }

    [Fact]
    public async Task A_push_already_queued_for_the_last_person_shows_nothing_to_the_next_and_keeps_their_own_subscription()
    {
        // The next person has turned notifications on, so this browser's subscription is theirs. The push was sent
        // for the last person before the switch.
        await DeliverPush($$"""{"kind":"run.finished","thread":"{{threadId}}","user":"{{anotherPerson}}"}""");

        (await ShownNotifications()).Should().BeEmpty("a wake sent for someone else is not described to the person signed in");
        wakeQueries.Should().Equal($"?kind=run.finished&thread={threadId}&user={anotherPerson}|{SignInCookie}|{TestSubscription}");
        (await SetSubscription(TestSubscription)).Should().Be(0, "the signed-in person's own subscription is not ended");

        await DeliverPush($$"""{"kind":"run.finished","thread":"{{threadId}}","user":"{{signedInPerson}}"}""");

        (await WaitForNotifications()).Should().Equal([$"Drover|Sample bot finished.|{Home}talk/{botId}"], "a wake sent for the signed-in person is described");
    }

    [Fact]
    public async Task A_push_with_no_subscription_to_name_shows_nothing_and_asks_the_server_nothing()
    {
        await SetSubscription(null);

        await DeliverPush($$"""{"kind":"run.finished","thread":"{{threadId}}","user":"{{signedInPerson}}"}""");

        (await ShownNotifications()).Should().BeEmpty();
        wakeQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task Tapping_a_notification_takes_the_open_window_to_that_conversation()
    {
        await page.EvaluateAsync(
            "() => new Promise(done => { navigator.serviceWorker.addEventListener('message', done, { once: true }); navigator.serviceWorker.controller.postMessage('ready?'); })");

        await page.EvaluateAsync("url => navigator.serviceWorker.controller.postMessage({ open: url })", $"{Home}talk/{botId}");

        // The worker moves the window itself, so the page sees no navigation of its own to wait for: watch the address.
        var started = TimeProvider.System.GetTimestamp();

        while (page.Url != $"{Origin}{Home}talk/{botId}" && TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), Xunit.TestContext.Current.CancellationToken);
        }

        page.Url.Should().Be($"{Origin}{Home}talk/{botId}");
    }

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        other = await Serve(app => app.MapGet("/data", (HttpContext http) =>
        {
            http.Response.Headers.AccessControlAllowOrigin = "*";
            return Results.Json(new { from = "another host" });
        }));
        shell = await Serve(app =>
        {
            app.Use(async (http, next) =>
            {
                http.Response.Headers.ContentSecurityPolicy = http.Request.Path == Home ? $"{WorkerPolicy} {Other}" : WorkerPolicy;
                await next();
            });
            MapClient(app, Home, Shell);
            app.MapGet("/api/push/wake", (HttpContext http) =>
            {
                var endpoint = http.Request.Headers["Push-Endpoint"].ToString();
                wakeQueries.Add($"{http.Request.QueryString}|{http.Request.Headers.Cookie}|{endpoint}");

                // The server answers only the person the wake was sent for, and only while they hold a subscription
                // for the address the worker names.
                if (http.Request.Query.TryGetValue("user", out var intended) && intended != signedInPerson.ToString())
                {
                    return Results.StatusCode(StatusCodes.Status409Conflict);
                }

                if (endpoint == AnotherPersonsSubscription)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                return http.Request.Query["thread"] == threadId.ToString() && http.Request.Headers.Cookie == SignInCookie
                    ? Results.Json(new { body = "Sample bot finished.", botId, threadId, isMain = true })
                    : Results.NotFound();
            });
            app.MapMethods("/api/data", ["GET", "POST"], () => Results.Json(new { from = "api" }));
            app.MapGet("/oauth/client.json", () => Results.Json(new { from = "oauth" }));
            app.MapGet($"{Home}broken.js", (HttpContext http) => http.Abort());
            app.MapGet($"{Home}bots/unreachable", (HttpContext http) => http.Abort());
            Routes(app);
        });

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = BrowserPath() });
        var context = await browser.NewContextAsync(new() { Permissions = ["notifications"] });
        page = await context.NewPageAsync();
        page.Response += (_, response) => responses.Add(response);
        await page.GotoAsync($"{Origin}{Home}");
        await page.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null");
    }

    // Each part is let go if it was made, so a start that failed half way leaves no server running.
    public async ValueTask DisposeAsync()
    {
        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        playwright?.Dispose();

        if (shell is not null)
        {
            await shell.DisposeAsync();
        }

        if (other is not null)
        {
            await other.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    #endregion

    #region Private Methods

    /// <summary>More routes for the server under test, for the cases of one client only.</summary>
    protected virtual void Routes(WebApplication app)
    {
    }

    /// <summary>
    /// A client at <paramref name="home"/>: its page (which signs the person in), its pages, its start script, the
    /// published worker and the notification script, and the list of files the worker stores.
    /// </summary>
    protected static void MapClient(WebApplication app, string home, string shellPage)
    {
        app.MapGet(home, (HttpContext http) =>
        {
            http.Response.Headers.SetCookie = $"{SignInCookie}; Path=/; SameSite=Strict";
            return Results.Content(shellPage, "text/html");
        });
        app.MapGet($"{home}index.html", () => Results.Content(shellPage, "text/html"));
        app.MapGet($"{home}talk/{{bot}}", () => Results.Content(shellPage, "text/html"));
        app.MapGet($"{home}register.js", () => Results.Content($"navigator.serviceWorker.register('{home}service-worker.js');", "text/javascript"));
        app.MapGet($"{home}service-worker.js", async () => Results.Content(
            $"{RejectionRecorder}{OpenRecorder}{PushDispatcher}{SubscriptionStub}{await File.ReadAllTextAsync(WebFile("service-worker.published.js"))}",
            "text/javascript"));
        app.MapGet($"{home}push-worker.js", async () => Results.Content(await File.ReadAllTextAsync(WebFile("push-worker.js")), "text/javascript"));
        app.MapGet($"{home}service-worker-assets.js", () => Results.Content(AssetsManifest(shellPage), "text/javascript"));
        app.MapGet($"{home}icon-192.png", () => Results.File(WebFile("icon-192.png"), "image/png"));
    }

    protected async Task<string> Fetch(string url, string method) =>
        await page.EvaluateAsync<string>(
            "async ([url, method]) => { try { return String((await fetch(url, { method })).status); } catch (e) { return 'failed: ' + e; } }",
            new[] { url, method });

    private static string WebFile(string name) => Path.Combine(RepositoryFiles.Root, "src", "Shared", "Web", name);

    /// <summary>Dispatch a push in the active browser worker and await the work registered by its handler.</summary>
    private async Task DeliverPush(string payload)
    {
        // DevTools reports that it accepted a push request without reporting whether delivery completed.
        await page.EvaluateAsync("""
            payload => new Promise((done, reject) => {
                const channel = new MessageChannel();
                channel.port1.onmessage = ({ data }) => {
                    channel.port1.close();
                    if (data === null) done();
                    else reject(new Error(data));
                };
                navigator.serviceWorker.controller.postMessage({ testPush: payload }, [channel.port2]);
            })
            """, payload).WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);
    }

    /// <summary>The notifications the worker shows now, after a push whose handling has finished.</summary>
    private async Task<string[]> ShownNotifications() =>
        await page.EvaluateAsync<string[]>(
            "async () => (await (await navigator.serviceWorker.ready).getNotifications()).map(n => `${n.title}|${n.body}|${n.data.url}`)");

    /// <summary>Sets the subscription the worker finds in its browser, or none, and returns how many times it has ended one.</summary>
    private async Task<int> SetSubscription(string? endpoint) =>
        await page.EvaluateAsync<int>(
            """
            endpoint => new Promise(done => {
                const channel = new MessageChannel();
                channel.port1.onmessage = ({ data }) => { channel.port1.close(); done(data); };
                navigator.serviceWorker.controller.postMessage({ subscription: endpoint }, [channel.port2]);
            })
            """,
            endpoint).WaitAsync(TimeSpan.FromSeconds(30), Xunit.TestContext.Current.CancellationToken);

    /// <summary>The notifications the worker shows, as title, body, and the address a tap opens.</summary>
    private async Task<string[]> WaitForNotifications()
    {
        var started = TimeProvider.System.GetTimestamp();

        while (true)
        {
            var shown = await page.EvaluateAsync<string[]>(
                "async () => (await (await navigator.serviceWorker.ready).getNotifications()).map(n => `${n.title}|${n.body}|${n.data.url}`)");

            if (shown.Length > 0 || TimeProvider.System.GetElapsedTime(started) > TimeSpan.FromSeconds(10))
            {
                return shown;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), Xunit.TestContext.Current.CancellationToken);
        }
    }

    /// <summary>The list the worker stores at install: the shell alone, with its hash, as the build writes it.</summary>
    private static string AssetsManifest(string shellPage) =>
        $"self.assetsManifest = {{ version: 'test', assets: [{{ url: 'index.html', hash: 'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(shellPage)))}' }}] }};";

    private static async Task<WebApplication> Serve(Action<WebApplication> routes)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        routes(app);
        await app.StartAsync(Xunit.TestContext.Current.CancellationToken);
        return app;
    }

    private static string BrowserPath() =>
        Environment.GetEnvironmentVariable("DROVER_TEST_CHROMIUM") is { Length: > 0 } chosen
            ? chosen
            : browserPaths.FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException($"No Chromium found. Set DROVER_TEST_CHROMIUM to one, or install Chrome or Chromium at one of: {string.Join(", ", browserPaths)}.");

    #endregion
}


/// <summary>The worker serving the web client, at the root.</summary>
public sealed class ServiceWorkerTests : ServiceWorkerScenarios
{
    #region Properties

    protected override string Home => "/";

    #endregion
}


/// <summary>
/// The same worker serving the native client's browser build under /native/, beside the web client's at the root. Each
/// keeps to its own client: the web client's worker answers nothing under /native/ and leaves its stored shell alone.
/// </summary>
public sealed class NativeServiceWorkerTests : ServiceWorkerScenarios
{
    #region Private Fields

    private const string WebShell = "<!DOCTYPE html><title>Web client shell</title><script src=\"/register.js\"></script>";

    #endregion

    #region Properties

    protected override string Home => "/native/";

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_web_clients_worker_leaves_the_native_clients_files_and_stored_shell_alone()
    {
        // The web client is opened in the same browser, and its worker takes control of its page.
        await Page.GotoAsync($"{Origin}/");
        await Page.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null");

        var seen = Responses.Count;

        (await Fetch($"{Origin}/native/register.js", "GET")).Should().Be("200");
        (await Fetch($"{Origin}/register.js", "GET")).Should().Be("200");

        var answers = Responses.Skip(seen).ToList();
        answers.Should().Contain(response => response.Url == $"{Origin}/native/register.js" && !response.FromServiceWorker,
            "the native client's files are not the web client's worker's to answer");
        answers.Should().Contain(response => response.Url == $"{Origin}/register.js" && response.FromServiceWorker);
        var shells = await Page.EvaluateAsync<string[]>("async () => (await caches.keys()).sort()");
        shells.Should().Equal("drover-shell-native-test", "drover-shell-test");
    }

    #endregion

    #region Private Methods

    protected override void Routes(WebApplication app) => MapClient(app, "/", WebShell);

    #endregion
}
