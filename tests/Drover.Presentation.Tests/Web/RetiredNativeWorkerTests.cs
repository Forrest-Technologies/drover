namespace Drover.Presentation.Tests.Web;

/// <summary>
/// A browser that installed the client while it was served under /native/ keeps that worker until the worker's script
/// changes. The server now answers that script with the retiring worker (src/Drover.Server/Clients), and this test runs
/// it in a real Chromium after a stand-in for the old worker: the old worker's stored shell is gone, the registration
/// is gone, and the open page is at the same address at the root.
/// </summary>
public sealed class RetiredNativeWorkerTests : IAsyncLifetime
{
    #region Private Fields

    private const string OldShell = "drover-shell-native-old";

    // What the old worker did that matters here: it stored its shell, took control of its pages, and answered fetches.
    private const string OldWorker = $$"""
        self.addEventListener("install", event => event.waitUntil(
            caches.open("{{OldShell}}").then(cache => cache.put("index.html", new Response("old shell"))).then(() => self.skipWaiting())));
        self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));
        self.addEventListener("fetch", () => { });
        """;

    private const string Shell = "<!DOCTYPE html><title>Retired worker test</title><script src=\"/native/register.js\"></script>";

    private const string RootShell = "<!DOCTYPE html><title>Client at the root</title>";

    private static readonly Guid botId = Guid.Parse("0196f1a0-0000-7000-8000-0000000000b1");

    private WebApplication? server;

    private IPlaywright? playwright;

    private IBrowser? browser;

    private IPage page = null!;

    private bool retired;

    #endregion

    #region Properties

    private string Origin => server!.Urls.Single();

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_retiring_worker_removes_the_old_worker_and_its_shell_and_moves_the_page_to_the_root()
    {
        (await page.EvaluateAsync<string[]>("async () => await caches.keys()")).Should().Contain(OldShell);

        retired = true;
        await page.EvaluateAsync("async () => { const registration = await navigator.serviceWorker.getRegistration('/native/'); await registration.update(); }");

        await WaitFor(() => Task.FromResult(page.Url == $"{Origin}/talk/{botId}"));
        await WaitFor(async () => (await page.EvaluateAsync<string[]>("async () => (await navigator.serviceWorker.getRegistrations()).map(r => r.scope)")).Length == 0);
        (await page.TitleAsync()).Should().Be("Client at the root");
        (await page.EvaluateAsync<string[]>("async () => await caches.keys()")).Should().NotContain(OldShell);
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
            app.MapGet("/native/talk/{bot}", () => retired
                ? Results.Redirect($"/talk/{botId}")
                : Results.Content(Shell, "text/html"));
            app.MapGet("/native/register.js", () => Results.Content("navigator.serviceWorker.register('/native/service-worker.js');", "text/javascript"));
            app.MapGet("/native/service-worker.js", async () => Results.Content(retired ? await RetiringWorker() : OldWorker, "text/javascript"));
            app.MapGet("/talk/{bot}", () => Results.Content(RootShell, "text/html"));
        });

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = ServiceWorkerScenarios.BrowserPath() });
        page = await (await browser.NewContextAsync()).NewPageAsync();
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

    private static Task<string> RetiringWorker() =>
        File.ReadAllTextAsync(Path.Combine(Repository.Root, "src", "Drover.Server", "Clients", "retired-native-worker.js"));

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
