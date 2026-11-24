namespace Drover.Presentation.Tests.Web;

/// <summary>
/// The page's start script (src/Shared/Web/boot.js) in a real Chromium, with the browser build's boot.css, served
/// under the content security policy Drover's server sends. The theme choice kept on the device decides the mode of
/// the first paint: the JSON the app writes today, the same JSON from before it carried a version, or the one word an
/// older build wrote. A person who follows the device gets no mode from the script, so the device's preference
/// decides; a value that is no choice is dark. The colors are the first surface of the default theme.
/// </summary>
public sealed class BootThemeTests : IAsyncLifetime
{
    #region Private Fields

    private const string Dark = "rgb(11, 12, 14)";

    private const string Light = "rgb(246, 246, 247)";

    private const string Black = "rgb(0, 0, 0)";

    private const string StartPage = "<!DOCTYPE html><title>Start</title><script src=\"/boot.js\"></script><link rel=\"stylesheet\" href=\"/boot.css\">";

    private const string EmptyPage = "<!DOCTYPE html><title>Empty</title>";

    private WebApplication? server;

    private IPlaywright? playwright;

    private IBrowser? browser;

    #endregion

    #region Properties

    private string Origin => server!.Urls.Single();

    #endregion

    #region Public Methods

    [Theory]
    [InlineData("light", "light", Light)]
    [InlineData("dark", "dark", Dark)]
    [InlineData("""{"themeId":"front-office","mode":"light","accent":"theme","version":2}""", "light", Light)]
    [InlineData("""{"themeId":"front-office","mode":"dark","accent":"theme","version":2}""", "dark", Dark)]
    [InlineData("""{"themeId":"front-office","mode":"black","accent":"theme","version":2}""", "black", Black)]
    [InlineData("""{"themeId":"night-yard","mode":"light"}""", "light", Light)]
    [InlineData("not a choice", "dark", Dark)]
    [InlineData("{not json", "dark", Dark)]
    [InlineData("""{"themeId":"front-office","mode":"sepia","version":2}""", "dark", Dark)]
    [InlineData("""{"themeId":"front-office","version":2}""", "dark", Dark)]
    [InlineData(null, "dark", Dark)]
    public async Task The_first_paint_is_in_the_mode_the_device_kept(string? stored, string mode, string surface)
    {
        // The device prefers the opposite, so the color can only come from what was kept.
        var page = await Start(stored, mode is "light" ? ColorScheme.Dark : ColorScheme.Light);

        (await Mode(page)).Should().Be(mode);
        (await Surface(page)).Should().Be(surface);
    }

    [Theory]
    [InlineData("system", ColorScheme.Light, Light)]
    [InlineData("system", ColorScheme.Dark, Dark)]
    [InlineData("""{"themeId":"front-office","mode":"system","accent":"theme","version":2}""", ColorScheme.Light, Light)]
    [InlineData("""{"themeId":"front-office","mode":"system","accent":"theme","version":2}""", ColorScheme.Dark, Dark)]
    [InlineData("""{"themeId":"night-yard","mode":"system"}""", ColorScheme.Light, Light)]
    public async Task A_person_who_follows_the_device_gets_no_mode_and_the_device_decides(string stored, ColorScheme device, string surface)
    {
        var page = await Start(stored, device);

        (await Mode(page)).Should().BeNull();
        (await Surface(page)).Should().Be(surface);
    }

    [Fact]
    public async Task The_script_runs_under_the_servers_policy_without_a_refusal()
    {
        var refusals = new List<string>();
        var page = await Start("""{"themeId":"front-office","mode":"light","accent":"theme","version":2}""", ColorScheme.Dark, refusals);

        (await Mode(page)).Should().Be("light");
        refusals.Should().BeEmpty();
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
            app.MapGet("/", () => Results.Content(StartPage, "text/html"));
            app.MapGet("/empty", () => Results.Content(EmptyPage, "text/html"));
            app.MapGet("/boot.js", async () => Results.Content(
                await File.ReadAllTextAsync(Path.Combine(Repository.Root, "src", "Shared", "Web", "boot.js")), "text/javascript"));
            app.MapGet("/boot.css", async () => Results.Content(
                await File.ReadAllTextAsync(Path.Combine(Repository.Root, "src", "Drover.Native", "Platforms", "WebAssembly", "wwwroot", "boot.css")), "text/css"));
        });

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = ServiceWorkerScenarios.BrowserPath() });
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

    /// <summary>Opens the start page on a device that kept <paramref name="stored"/> and prefers <paramref name="device"/>.</summary>
    private async Task<IPage> Start(string? stored, ColorScheme device, List<string>? refusals = null)
    {
        var page = await (await browser!.NewContextAsync(new() { ColorScheme = device })).NewPageAsync();
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.Ordinal))
            {
                refusals?.Add(message.Text);
            }
        };

        await page.GotoAsync($"{Origin}/empty");
        if (stored is not null)
        {
            await page.EvaluateAsync("value => localStorage.setItem('drover.theme', value)", stored);
        }

        await page.GotoAsync($"{Origin}/");
        return page;
    }

    private static Task<string?> Mode(IPage page) =>
        page.EvaluateAsync<string?>("() => document.documentElement.getAttribute('data-theme')");

    private static Task<string> Surface(IPage page) =>
        page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).backgroundColor");

    #endregion
}
