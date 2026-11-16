namespace Drover.Ui.Tests.Web;

/// <summary>
/// The published service worker in a real Chromium, served by a small server in this test that sends the worker the
/// same content security policy Drover's server does. Requests to another host, the server's API and sign-in
/// callbacks, and anything that is not a GET go to the network without the worker; a request it cannot answer leaves
/// no unhandled rejection; a page load the server cannot answer gets the stored shell. A push wakes the worker, which
/// asks this server with the person's own cookie what to show, and tapping it opens that conversation. The browser is
/// DROVER_TEST_CHROMIUM, or the first Chromium or Chrome at a usual path.
/// </summary>
public sealed class ServiceWorkerTests : IAsyncLifetime
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

    /// <summary>The person's sign-in, as a cookie the worker's own request to the server must carry.</summary>
    private const string SignInCookie = "drover-test-session=signed-in";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000b1");

    private static readonly Guid threadId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000c1");

    /// <summary>The shell the worker stores at install, as Drover's index.html is, and serves when a page cannot load.</summary>
    private const string Shell = "<!DOCTYPE html><title>Worker test shell</title><script src=\"/register.js\"></script>";

    private static readonly string[] browserPaths = ["/opt/pw-browsers/chromium", "/usr/bin/google-chrome", "/usr/bin/chromium", "/usr/bin/chromium-browser"];

    private readonly List<IResponse> responses = [];

    private WebApplication? other;

    private WebApplication? shell;

    private IPlaywright? playwright;

    private IBrowser? browser;

    private IPage page = null!;

    private ICDPSession devTools = null!;

    private readonly List<string> wakeQueries = [];

    #endregion

    #region Properties

    private string Other => other!.Urls.Single();

    private string Origin => shell!.Urls.Single();

    #endregion

    #region Public Methods

    [Theory]
    [InlineData("another host", "GET", false)]
    [InlineData("/api/data", "GET", false)]
    [InlineData("/api/data", "POST", false)]
    [InlineData("/oauth/client.json", "GET", false)]
    [InlineData("/register.js", "GET", true)]
    public async Task Only_drovers_own_files_and_pages_are_answered_by_the_worker(string address, string method, bool byWorker)
    {
        var url = address == "another host" ? $"{Other}/data" : $"{Origin}{address}";

        (await Fetch(url, method)).Should().Be("200", "the request reaches its server, with or without the worker");

        responses.Should().Contain(response => response.Url == url && response.Request.Method == method && response.FromServiceWorker == byWorker);
    }

    [Fact]
    public async Task A_request_the_worker_cannot_answer_fails_without_an_unhandled_rejection()
    {
        (await Fetch($"{Origin}/broken.js", "GET")).Should().StartWith("failed");

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
            $"{Origin}/bots/unreachable");
        asked.Should().Be($"200 {Shell}", "a GET for a page address that is not a navigation is still a page load");

        await page.GotoAsync($"{Origin}/bots/unreachable");
        (await page.TitleAsync()).Should().Be("Worker test shell");
    }

    [Fact]
    public async Task A_push_shows_what_the_server_says_asked_for_with_the_persons_own_sign_in()
    {
        await DeliverPush($$"""{"kind":"run.finished","thread":"{{threadId}}"}""");

        var shown = await WaitForNotifications();

        shown.Should().Equal($"Drover|Sample bot finished.|/talk/{botId}");
        wakeQueries.Should().Equal($"?kind=run.finished&thread={threadId}|{SignInCookie}");
    }

    [Fact]
    public async Task A_push_the_server_does_not_explain_still_shows_without_any_of_the_conversation()
    {
        await DeliverPush($$"""{"kind":"run.finished","thread":"{{Guid.Empty}}"}""");

        var shown = await WaitForNotifications();

        shown.Should().Equal("Drover|Something changed. Open Drover to see it.|/");
    }

    [Fact]
    public async Task Tapping_a_notification_takes_the_open_window_to_that_conversation()
    {
        await page.EvaluateAsync(
            "() => new Promise(done => { navigator.serviceWorker.addEventListener('message', done, { once: true }); navigator.serviceWorker.controller.postMessage('ready?'); })");

        await page.EvaluateAsync("url => navigator.serviceWorker.controller.postMessage({ open: url })", $"/talk/{botId}");

        // The worker moves the window itself, so the page sees no navigation of its own to wait for: watch the address.
        var started = TimeProvider.System.GetTimestamp();

        while (page.Url != $"{Origin}/talk/{botId}" && TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), Xunit.TestContext.Current.CancellationToken);
        }

        page.Url.Should().Be($"{Origin}/talk/{botId}");
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
                http.Response.Headers.ContentSecurityPolicy = http.Request.Path == "/" ? $"{WorkerPolicy} {Other}" : WorkerPolicy;
                await next();
            });
            app.MapGet("/", (HttpContext http) =>
            {
                http.Response.Headers.SetCookie = $"{SignInCookie}; Path=/; SameSite=Strict";
                return Results.Content(Shell, "text/html");
            });
            app.MapGet("/index.html", () => Results.Content(Shell, "text/html"));
            app.MapGet("/talk/{bot}", () => Results.Content(Shell, "text/html"));
            app.MapGet("/register.js", () => Results.Content("navigator.serviceWorker.register('/service-worker.js');", "text/javascript"));
            app.MapGet("/service-worker.js", async () => Results.Content(
                $"{RejectionRecorder}{OpenRecorder}{await File.ReadAllTextAsync(WebFile("service-worker.published.js"))}",
                "text/javascript"));
            app.MapGet("/push-worker.js", async () => Results.Content(await File.ReadAllTextAsync(WebFile("push-worker.js")), "text/javascript"));
            app.MapGet("/api/push/wake", (HttpContext http) =>
            {
                wakeQueries.Add($"{http.Request.QueryString}|{http.Request.Headers.Cookie}");
                return http.Request.Query["thread"] == threadId.ToString() && http.Request.Headers.Cookie == SignInCookie
                    ? Results.Json(new { body = "Sample bot finished.", botId, threadId, isMain = true })
                    : Results.NotFound();
            });
            app.MapGet("/service-worker-assets.js", () => Results.Content(AssetsManifest(), "text/javascript"));
            app.MapMethods("/api/data", ["GET", "POST"], () => Results.Json(new { from = "api" }));
            app.MapGet("/oauth/client.json", () => Results.Json(new { from = "oauth" }));
            app.MapGet("/broken.js", (HttpContext http) => http.Abort());
            app.MapGet("/bots/unreachable", (HttpContext http) => http.Abort());
        });

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = BrowserPath() });
        var context = await browser.NewContextAsync(new() { Permissions = ["notifications"] });
        page = await context.NewPageAsync();
        page.Response += (_, response) => responses.Add(response);
        await page.GotoAsync($"{Origin}/");
        await page.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null");
        devTools = await context.NewCDPSessionAsync(page);
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
    }

    #endregion

    #region Private Methods

    private async Task<string> Fetch(string url, string method) =>
        await page.EvaluateAsync<string>(
            "async ([url, method]) => { try { return String((await fetch(url, { method })).status); } catch (e) { return 'failed: ' + e; } }",
            new[] { url, method });

    private static string WebFile(string name) => Path.Combine(RepositoryFiles.Root, "src", "Drover.Web", "wwwroot", name);

    /// <summary>A real push, as a push service delivers it, through the browser's developer protocol.</summary>
    private async Task DeliverPush(string payload)
    {
        var registered = new TaskCompletionSource<string>();
        devTools.Event("ServiceWorker.workerRegistrationUpdated").OnEvent += (_, update) =>
        {
            if (update?.GetProperty("registrations").EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } registration)
            {
                registered.TrySetResult(registration.GetProperty("registrationId").GetString()!);
            }
        };
        await devTools.SendAsync("ServiceWorker.enable");
        var registrationId = await registered.Task.WaitAsync(TimeSpan.FromSeconds(10), Xunit.TestContext.Current.CancellationToken);
        await devTools.SendAsync("ServiceWorker.deliverPushMessage", new()
        {
            ["origin"] = Origin,
            ["registrationId"] = registrationId,
            ["data"] = payload,
        });
    }

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
    private static string AssetsManifest() =>
        $"self.assetsManifest = {{ version: 'test', assets: [{{ url: 'index.html', hash: 'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Shell)))}' }}] }};";

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
