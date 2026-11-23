namespace Drover.Presentation.Tests.Web;

/// <summary>
/// An app installed from /native/ before the client moved to the root keeps working. These tests install it in a
/// real Chromium through the browser's own app install (the DevTools protocol's PWA domain), with the manifest the
/// client shipped under /native/, then serve what the server serves now: the moving page and the migration manifest
/// at /native/ (src/Drover.Server/Clients), a redirect for every other address under it, and the client's own manifest
/// at the root. Launched, the installed app opens the client at the root, and the browser moves the app's scope to
/// the root, so from then on it opens there in its own window. The page at the root is a stand-in that links the
/// client's real manifest.
/// </summary>
public sealed class InstalledAppMigrationTests : IAsyncLifetime
{
    #region Private Fields

    // The manifest the client was published with while it was served under /native/.
    private const string OldManifest = """
        {"name":"Drover","short_name":"Drover","description":"Run your own crew of bots.","id":"/native/","start_url":"/native/","scope":"/native/","display":"standalone","background_color":"#05070A","theme_color":"#05070A","icons":[{"src":"icon-192.png","sizes":"192x192","type":"image/png"},{"src":"icon-512.png","sizes":"512x512","type":"image/png"},{"src":"icon-maskable-512.png","sizes":"512x512","type":"image/png","purpose":"maskable"}]}
        """;

    private const string OldPage = "<!DOCTYPE html><title>Old client</title><link rel=\"manifest\" href=\"/native/manifest.webmanifest\">";

    private const string RootPage = "<!DOCTYPE html><title>Client at the root</title><link rel=\"manifest\" href=\"/manifest.webmanifest\">";

    private static readonly string[] icons = ["icon-192.png", "icon-512.png", "icon-maskable-512.png", "icon.svg"];

    private readonly string profile = Directory.CreateTempSubdirectory("drover-installed-app-").FullName;

    private WebApplication? server;

    private IPlaywright? playwright;

    private IBrowserContext? browser;

    private ICDPSession devTools = null!;

    private bool moved;

    #endregion

    #region Properties

    private string Origin => server!.Urls.Single();

    private string OldAppId => $"{Origin}/native/";

    #endregion

    #region Public Methods

    [Fact]
    public async Task An_app_installed_from_the_former_address_opens_the_client_at_the_root()
    {
        moved = true;

        await Launch();

        (await WaitForRootPage()).Should().BeTrue("the installed app's start page moves itself to the root");
    }

    [Fact]
    public async Task The_installed_app_is_moved_to_the_root_so_it_opens_there_in_its_own_window()
    {
        (await LaunchAt("/")).Should().Contain("is not in the scope", "before the move, the root is outside the installed app");

        moved = true;
        var refusal = "not launched yet";

        // The browser reads the new manifest while the moving page is open. Each launch of the app is a chance to.
        for (var launch = 0; launch < 3 && refusal is not null; launch++)
        {
            await Launch();
            await WaitForRootPage();
            refusal = await LaunchAt("/");
        }

        refusal.Should().BeNull("the migration manifest keeps the app's id and moves its scope to the root");
        (await WaitForRootPage()).Should().BeTrue();
    }

    [Fact]
    public void The_clients_manifest_keeps_the_identity_the_web_clients_installed_app_had()
    {
        // The retired web client's manifest had the id "/", start and scope the root: its installed apps stay the same app.
        using var manifest = JsonDocument.Parse(File.ReadAllText(ClientManifest()));

        manifest.RootElement.GetProperty("id").GetString().Should().Be("/");
        manifest.RootElement.GetProperty("start_url").GetString().Should().Be("/");
        manifest.RootElement.GetProperty("scope").GetString().Should().Be("/");
    }

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        server = await ServiceWorkerScenarios.Serve(app =>
        {
            app.MapGet("/native/", (HttpContext http) => !moved
                ? Results.Content(OldPage, "text/html")
                : http.Request.QueryString.HasValue ? Results.Redirect($"/{http.Request.QueryString}") : Results.Content(ServerFile("retired-native-page.html"), "text/html"));
            app.MapGet("/native/manifest.webmanifest", () => Results.Content(
                moved ? ServerFile("retired-native-manifest.webmanifest") : OldManifest, "application/manifest+json"));
            app.MapGet("/native/{name}", (string name) => moved ? Results.Redirect($"/{name}") : WebFile(name));
            app.MapGet("/manifest.webmanifest", () => Results.Content(File.ReadAllText(ClientManifest()), "application/manifest+json"));
            app.MapGet("/boot.css", () => Results.Content(string.Empty, "text/css"));
            app.MapGet("/{name}", WebFile);
            app.MapGet("/", () => Results.Content(RootPage, "text/html"));
        });

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchPersistentContextAsync(profile, new() { ExecutablePath = ServiceWorkerScenarios.BrowserPath() });
        var page = browser.Pages is [var first, ..] ? first : await browser.NewPageAsync();
        await page.GotoAsync(OldAppId);
        devTools = await browser.NewCDPSessionAsync(page);
        await devTools.SendAsync("PWA.install", new Dictionary<string, object> { ["manifestId"] = OldAppId });
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

        Directory.Delete(profile, recursive: true);
    }

    #endregion

    #region Private Methods

    /// <summary>Opens the installed app as a person does, at its start address.</summary>
    private async Task Launch() =>
        await devTools.SendAsync("PWA.launch", new Dictionary<string, object> { ["manifestId"] = OldAppId });

    /// <summary>Opens the installed app at an address. Null when it opened; the browser's refusal when it did not.</summary>
    private async Task<string?> LaunchAt(string path)
    {
        try
        {
            await devTools.SendAsync("PWA.launch", new Dictionary<string, object> { ["manifestId"] = OldAppId, ["url"] = $"{Origin}{path}" });
            return null;
        }
        catch (PlaywrightException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>Waits for a window showing the client at the root, and closes it so the next one can be told apart.</summary>
    private async Task<bool> WaitForRootPage()
    {
        var started = TimeProvider.System.GetTimestamp();

        while (TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(20))
        {
            foreach (var window in browser!.Pages.Where(window => window.Url == $"{Origin}/"))
            {
                if (await window.TitleAsync() == "Client at the root")
                {
                    await window.CloseAsync();
                    return true;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), Xunit.TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static string ServerFile(string name) =>
        File.ReadAllText(Path.Combine(Repository.Root, "src", "Drover.Server", "Clients", name));

    private static string ClientManifest() =>
        Path.Combine(Repository.NativeFolder, "Platforms", "WebAssembly", "wwwroot", "manifest.webmanifest");

    private static IResult WebFile(string name) =>
        icons.Contains(name)
            ? Results.File(Path.Combine(Repository.Root, "src", "Shared", "Web", name), name.EndsWith(".svg", StringComparison.Ordinal) ? "image/svg+xml" : "image/png")
            : Results.NotFound();

    #endregion
}
