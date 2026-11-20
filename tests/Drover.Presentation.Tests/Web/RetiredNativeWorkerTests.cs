namespace Drover.Presentation.Tests.Web;

/// <summary>
/// A browser that installed the client while it was served under /native/ keeps that worker until the worker's script
/// changes. The server now answers that script with the retiring worker (src/Drover.Server/Clients), and these tests
/// run it in a real Chromium after a stand-in for the old worker: the old worker's stored shell is gone, the
/// registration is gone, an open page is at the same address at the root, the moving page is left to move itself, and
/// a push subscription the old worker held leaves the note the client at the root reads (push.js) to take it up again.
/// The push service is a stand-in: the old worker's subscription is a sample object, so no push service is reached.
/// </summary>
public sealed class RetiredNativeWorkerTests : IAsyncLifetime
{
    #region Private Fields

    private const string OldShell = "drover-shell-native-old";

    private const string MovedNote = "drover-push-moved";

    // What the old worker did that matters here: it stored its shell, took control of its pages, and answered fetches.
    private const string OldWorker = $$"""
        self.addEventListener("install", event => event.waitUntil(
            caches.open("{{OldShell}}").then(cache => cache.put("index.html", new Response("old shell"))).then(() => self.skipWaiting())));
        self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));
        self.addEventListener("fetch", () => { });
        """;

    // Put before the retiring worker when the old worker held a subscription: a sample one, which records that it ended.
    private const string SampleSubscription = """
        Object.defineProperty(self.registration.pushManager, "getSubscription", { value: async () => ({
            endpoint: "https://push.example.test/sample-moved",
            unsubscribe: async () => { await caches.open("test-unsubscribed"); return true; },
        }) });

        """;

    private const string Shell = "<!DOCTYPE html><title>Retired worker test</title><script src=\"/native/register.js\"></script>";

    private const string MovingPage = "<!DOCTYPE html><title>Moving</title>";

    private const string RootShell = "<!DOCTYPE html><title>Client at the root</title>";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000b1");

    private WebApplication? server;

    private IPlaywright? playwright;

    private IBrowser? browser;

    private IPage page = null!;

    private bool retired;

    private bool subscribed;

    #endregion

    #region Properties

    private string Origin => server!.Urls.Single();

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_retiring_worker_removes_the_old_worker_and_its_shell_and_moves_the_page_to_the_root()
    {
        (await page.EvaluateAsync<string[]>("async () => await caches.keys()")).Should().Contain(OldShell);

        await Retire();

        (await page.TitleAsync()).Should().Be("Client at the root");
        (await page.EvaluateAsync<string[]>("async () => await caches.keys()")).Should().NotContain(OldShell);
    }

    [Fact]
    public async Task The_moving_page_is_left_to_move_itself()
    {
        var moving = await page.Context.NewPageAsync();
        await moving.GotoAsync($"{Origin}/native/");
        await moving.WaitForFunctionAsync("() => navigator.serviceWorker.controller !== null");

        await Retire();

        moving.Url.Should().Be($"{Origin}/native/", "an installed app must stay there long enough to read its new manifest");
    }

    [Fact]
    public async Task A_subscription_the_old_worker_held_leaves_a_note_the_client_at_the_root_takes_up()
    {
        subscribed = true;

        await Retire();

        (await page.EvaluateAsync<string[]>("async () => await caches.keys()")).Should().Contain([MovedNote, "test-unsubscribed"]);
        (await Moved()).Should().Be("permission-kept", "the browser still allows notifications, so nothing needs asking");
        (await MovedEndpoint()).Should().Be("https://push.example.test/sample-moved", "the server is asked whose it was");

        await page.EvaluateAsync("async () => (await import('/push.js')).forgetMove()");

        (await Moved()).Should().Be("none");
        (await MovedEndpoint()).Should().BeNull();
    }

    [Fact]
    public async Task A_browser_that_no_longer_allows_notifications_is_told_so_instead_of_asked()
    {
        subscribed = true;

        await Retire();
        await page.Context.ClearPermissionsAsync();

        (await Moved()).Should().Be("permission-lost");
    }

    [Fact]
    public async Task An_old_worker_without_a_subscription_leaves_no_note()
    {
        await Retire();

        (await page.EvaluateAsync<string[]>("async () => await caches.keys()")).Should().NotContain(MovedNote);
        (await Moved()).Should().Be("none");
    }

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        server = await ServiceWorkerScenarios.Serve(app =>
        {
            app.Use(async (http, next) =>
            {
                http.Response.Headers.ContentSecurityPolicy = "default-src 'self'; connect-src 'self'";
                await next();
            });
            app.MapGet("/native/", () => Results.Content(retired ? MovingPage : Shell, "text/html"));
            app.MapGet("/native/talk/{bot}", () => retired
                ? Results.Redirect($"/talk/{botId}")
                : Results.Content(Shell, "text/html"));
            app.MapGet("/native/register.js", () => Results.Content("navigator.serviceWorker.register('/native/service-worker.js');", "text/javascript"));
            app.MapGet("/native/service-worker.js", async () => Results.Content(retired ? await RetiringWorker() : OldWorker, "text/javascript"));
            app.MapGet("/talk/{bot}", () => Results.Content(RootShell, "text/html"));
            app.MapGet("/push.js", async () => Results.Content(
                await File.ReadAllTextAsync(Path.Combine(Repository.Root, "src", "Shared", "Web", "push.js")), "text/javascript"));
        });

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = ServiceWorkerScenarios.BrowserPath() });
        page = await (await browser.NewContextAsync(new() { Permissions = ["notifications"] })).NewPageAsync();
        await page.GotoAsync($"{Origin}/native/talk/{botId}");
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

        if (server is not null)
        {
            await server.DisposeAsync();
        }
    }

    #endregion

    #region Private Methods

    private async Task<string> RetiringWorker()
    {
        var worker = await File.ReadAllTextAsync(Path.Combine(Repository.Root, "src", "Drover.Server", "Clients", "retired-native-worker.js"));
        return subscribed ? $"{SampleSubscription}{worker}" : worker;
    }

    /// <summary>The server answers with the retiring worker; the page asks for an update and is moved to the root.</summary>
    private async Task Retire()
    {
        retired = true;
        await page.EvaluateAsync("async () => { const registration = await navigator.serviceWorker.getRegistration('/native/'); await registration.update(); }");

        await WaitFor(() => Task.FromResult(page.Url == $"{Origin}/talk/{botId}"));
        await WaitFor(async () => (await page.EvaluateAsync<string[]>("async () => (await navigator.serviceWorker.getRegistrations()).map(r => r.scope)")).Length == 0);
    }

    /// <summary>What the client at the root reads from push.js about a subscription the move ended.</summary>
    private Task<string> Moved() => page.EvaluateAsync<string>("async () => (await import('/push.js')).moved()");

    private Task<string?> MovedEndpoint() =>
        page.EvaluateAsync<string?>("async () => (await import('/push.js')).movedEndpoint()");

    private static async Task WaitFor(Func<Task<bool>> done)
    {
        var started = TimeProvider.System.GetTimestamp();

        while (!await done() && TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50), Xunit.TestContext.Current.CancellationToken);
        }

        (await done()).Should().BeTrue();
    }

    #endregion
}
