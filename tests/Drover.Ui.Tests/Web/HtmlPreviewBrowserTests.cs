namespace Drover.Ui.Tests.Web;

/// <summary>
/// A hostile HTML preview served from a second origin into a real Chromium browser under the application's and the
/// preview endpoint's exact content security policies and iframe attributes (<c>HtmlPreview</c>).
/// </summary>
public sealed class HtmlPreviewBrowserTests : IAsyncLifetime
{
    #region Private Fields

    private const string SignInCookie = "drover-test-session=signed-in";

    private const string PreviewOriginCookie = "preview-origin-cookie=should-not-be-sent";

    private readonly List<CapturedRequest> previewRequests = [];

    private readonly List<string> unlistedRequests = [];

    private WebApplication? appServer;

    private WebApplication? previewServer;

    private WebApplication? unlistedServer;

    private IPlaywright? playwright;

    private IBrowser? browser;

    private IPage page = null!;

    private string appBasePolicy = string.Empty;

    private string previewBasePolicy = string.Empty;

    #endregion

    #region Properties

    private string AppOrigin => appServer!.Urls.Single();

    private string PreviewOrigin => previewServer!.Urls.Single();

    private string UnlistedOrigin => unlistedServer!.Urls.Single();

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_hostile_preview_on_a_second_origin_loads_in_an_opaque_credentialless_sandbox_with_no_parent_cookie_storage_or_network_access()
    {
        VerifyHtmlPreviewAttributeOrder();

        // Seed a cookie on the preview origin first so Chromium would send it if credentialless were missing.
        await page.GotoAsync($"{PreviewOrigin}/seed-cookie");
        await page.GotoAsync($"{AppOrigin}/");
        await page.EvaluateAsync("() => localStorage.setItem('drover.secret', 'top-secret')");

        await AttachPreviewFrame($"{PreviewOrigin}/preview/file?grant=one");

        var frame = page.Frames.Single(candidate => candidate != page.MainFrame);
        await frame.Locator("#preview-heading").WaitForAsync();
        (await frame.Locator("#preview-heading").InnerTextAsync()).Should().Be("Hostile preview");
        (await page.TitleAsync()).Should().Be("Drover app");

        var request = previewRequests.Should().ContainSingle().Subject;
        request.PathAndQuery.Should().Be("/preview/file?grant=one");
        request.Cookie.Should().BeEmpty("a credentialless preview frame must not carry cookies");
        request.Referer.Should().BeEmpty("referrerpolicy=no-referrer must suppress the referrer header");

        var probe = await frame.EvaluateAsync<string[]>(
            """
            async beaconUrl => {
                const check = action => {
                    try {
                        action();
                        return 'allowed';
                    } catch (error) {
                        return error?.name || String(error);
                    }
                };
                let fetchOutcome = 'allowed';
                try {
                    await fetch(beaconUrl);
                } catch (error) {
                    fetchOutcome = error?.name || String(error);
                }
                return [
                    window.origin,
                    String(Boolean(window.__scriptRan)),
                    check(() => window.parent.document.title),
                    check(() => window.top.location.href),
                    check(() => document.cookie),
                    check(() => window.localStorage.getItem('drover.secret')),
                    fetchOutcome
                ];
            }
            """,
            $"{UnlistedOrigin}/fetch-probe");

        probe[0].Should().Be("null");
        probe[1].Should().Be("false");
        probe[2].Should().Be("SecurityError");
        probe[3].Should().Be("SecurityError");
        probe[4].Should().Be("SecurityError");
        probe[5].Should().Be("SecurityError");
        probe[6].Should().NotBe("allowed");
        unlistedRequests.Should().BeEmpty("a sandboxed preview with default-src 'none' and connect-src 'none' must make no network request");
    }

    [Fact]
    public async Task The_app_content_security_policy_refuses_to_frame_an_origin_not_named_in_frame_src()
    {
        await page.GotoAsync($"{AppOrigin}/");
        var unlistedViolation = await AttachBlockedFrame($"{UnlistedOrigin}/preview/file?grant=unlisted");

        unlistedViolation.Should().StartWith("frame-src|");
        unlistedRequests.Should().BeEmpty("the browser must block the unlisted origin before loading its document");

        await page.GotoAsync($"{AppOrigin}/no-preview-origin");
        var disabledViolation = await AttachBlockedFrame($"{PreviewOrigin}/preview/file?grant=disabled");

        disabledViolation.Should().StartWith("frame-src|");
        previewRequests.Should().BeEmpty("frame-src 'none' must refuse the preview frame before requesting the grant");
    }

    #endregion

    #region Interface Implementations

    public async ValueTask InitializeAsync()
    {
        appBasePolicy = ReadAppBasePolicy();
        previewBasePolicy = ReadPreviewBasePolicy();

        unlistedServer = await Serve(app =>
        {
            app.MapGet("/{**path}", (HttpContext http) =>
            {
                unlistedRequests.Add($"{http.Request.Path}{http.Request.QueryString}");
                return Results.Content("<p>Unlisted origin</p>", "text/html");
            });
        });

        previewServer = await Serve(app =>
        {
            app.MapGet("/seed-cookie", (HttpContext http) =>
            {
                http.Response.Headers.SetCookie = $"{PreviewOriginCookie}; Path=/; SameSite=Lax";
                return Results.Content("<!DOCTYPE html><title>Seeded</title>", "text/html");
            });
            app.MapGet("/preview/file", (HttpContext http) =>
            {
                previewRequests.Add(new CapturedRequest(
                    $"{http.Request.Path}{http.Request.QueryString}",
                    http.Request.Headers.Cookie.ToString(),
                    http.Request.Headers.Referer.ToString()));

                http.Response.Headers.ContentSecurityPolicy = $"{previewBasePolicy}; frame-ancestors {AppOrigin}";
                http.Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
                http.Response.Headers["Referrer-Policy"] = "no-referrer";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                http.Response.Headers.CacheControl = "no-store";

                var hostileHtml =
                    $$"""
                    <!DOCTYPE html>
                    <html>
                    <body>
                      <h1 id="preview-heading">Hostile preview</h1>
                      <script>
                        window.__scriptRan = true;
                        parent.document.title = 'compromised';
                        fetch('{{UnlistedOrigin}}/script-beacon');
                      </script>
                      <img src="{{UnlistedOrigin}}/image-beacon.png" onerror="window.__scriptRan = true" />
                    </body>
                    </html>
                    """;
                return Results.Content(hostileHtml, "text/html; charset=utf-8");
            });
        });

        appServer = await Serve(app =>
        {
            app.MapGet("/", (HttpContext http) =>
            {
                http.Response.Headers.SetCookie = $"{SignInCookie}; Path=/; SameSite=Strict";
                http.Response.Headers.ContentSecurityPolicy = $"{appBasePolicy}; frame-src {PreviewOrigin}";
                http.Response.Headers["Referrer-Policy"] = "no-referrer";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return Results.Content("<!DOCTYPE html><title>Drover app</title><div id=\"host\"></div>", "text/html");
            });
            app.MapGet("/no-preview-origin", (HttpContext http) =>
            {
                http.Response.Headers.SetCookie = $"{SignInCookie}; Path=/; SameSite=Strict";
                http.Response.Headers.ContentSecurityPolicy = $"{appBasePolicy}; frame-src 'none'";
                http.Response.Headers["Referrer-Policy"] = "no-referrer";
                http.Response.Headers["X-Content-Type-Options"] = "nosniff";
                return Results.Content("<!DOCTYPE html><title>Drover app</title><div id=\"host\"></div>", "text/html");
            });
        });

        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = TestBrowser.Path() });
        var context = await browser.NewContextAsync();
        page = await context.NewPageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        playwright?.Dispose();

        if (appServer is not null)
        {
            await appServer.DisposeAsync();
        }

        if (previewServer is not null)
        {
            await previewServer.DisposeAsync();
        }

        if (unlistedServer is not null)
        {
            await unlistedServer.DisposeAsync();
        }
    }

    #endregion

    #region Private Methods

    private static void VerifyHtmlPreviewAttributeOrder()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "src", "Drover.Native", "Chat", "HtmlPreview.cs"));
        var allowIndex = source.IndexOf("(\"allow\", string.Empty)", StringComparison.Ordinal);
        var credentiallessIndex = source.IndexOf("(\"credentialless\", string.Empty)", StringComparison.Ordinal);
        var referrerIndex = source.IndexOf("(\"referrerpolicy\", \"no-referrer\")", StringComparison.Ordinal);
        var sandboxIndex = source.IndexOf("(\"sandbox\", string.Empty)", StringComparison.Ordinal);
        var srcIndex = source.IndexOf("(\"src\", address)", StringComparison.Ordinal);
        var attachIndex = source.IndexOf("host.Content = frame;", StringComparison.Ordinal);

        allowIndex.Should().BeGreaterThanOrEqualTo(0);
        credentiallessIndex.Should().BeGreaterThan(allowIndex);
        referrerIndex.Should().BeGreaterThan(credentiallessIndex);
        sandboxIndex.Should().BeGreaterThan(referrerIndex);
        srcIndex.Should().BeGreaterThan(sandboxIndex, "sandbox and credentialless must be set before src");
        attachIndex.Should().BeGreaterThan(srcIndex, "the frame must be configured before it is attached to the visual tree");
    }

    private async Task AttachPreviewFrame(string address) =>
        await page.EvaluateAsync(
            """
            url => new Promise((resolve, reject) => {
                const frame = document.createElement('iframe');
                frame.setAttribute('allow', '');
                frame.setAttribute('credentialless', '');
                frame.setAttribute('referrerpolicy', 'no-referrer');
                frame.setAttribute('sandbox', '');
                frame.setAttribute('src', url);
                frame.setAttribute('title', 'Preview of report.html');
                frame.addEventListener('load', () => resolve(), { once: true });
                frame.addEventListener('error', () => reject(new Error('Preview frame failed to load.')), { once: true });
                document.getElementById('host').appendChild(frame);
            })
            """,
            address);

    private Task<string> AttachBlockedFrame(string address) =>
        page.EvaluateAsync<string>(
            """
            url => new Promise(resolve => {
                document.addEventListener(
                    'securitypolicyviolation',
                    event => resolve(`${event.violatedDirective}|${event.blockedURI}`),
                    { once: true });
                const frame = document.createElement('iframe');
                frame.setAttribute('allow', '');
                frame.setAttribute('credentialless', '');
                frame.setAttribute('referrerpolicy', 'no-referrer');
                frame.setAttribute('sandbox', '');
                frame.setAttribute('src', url);
                document.getElementById('host').appendChild(frame);
            })
            """,
            address);

    private static string ReadAppBasePolicy()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "src", "Drover.Server", "Security", "SecurityHeaders.cs"));
        var block = Regex.Match(source, @"private const string ContentSecurityPolicy =([\s\S]*?);\r?\n").Groups[1].Value;
        return string.Concat(Regex.Matches(block, "\"([^\"\\n]*)\"").Select(match => match.Groups[1].Value));
    }

    private static string ReadPreviewBasePolicy()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "src", "Drover.Server", "Files", "PreviewFileEndpoints.cs"));
        var block = Regex.Match(source, @"private static readonly string previewPolicy = string\.Join\(' ',([\s\S]*?)\);").Groups[1].Value;
        return string.Join(' ', Regex.Matches(block, "\"([^\"\\n]*)\"").Select(match => match.Groups[1].Value));
    }

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


    #endregion

    #region Nested Types

    private sealed record CapturedRequest(string PathAndQuery, string Cookie, string Referer);

    #endregion
}
