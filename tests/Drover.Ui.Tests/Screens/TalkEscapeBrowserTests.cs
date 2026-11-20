namespace Drover.Ui.Tests.Screens;

/// <summary>
/// Exercises <c>Talk.razor.js</c> directly in a real Chromium DOM: IME composition, drafts in rail fields and
/// attached files, and browsers where <c>:popover-open</c> is not a supported selector.
/// </summary>
public sealed class TalkEscapeBrowserTests : IAsyncLifetime
{
    #region Private Fields

    private static readonly string[] browserPaths =
    [
        "/opt/pw-browsers/chromium",
        "/usr/bin/google-chrome",
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser",
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    ];

    private IPlaywright? playwright;

    private IBrowser? browser;

    private IPage page = null!;

    private string talkScript = string.Empty;

    #endregion

    #region Public Methods

    [Fact]
    public async Task Escape_while_composing_with_a_draft_or_without_popover_selector_support_behaves_safely()
    {
        await page.SetContentAsync("""
            <!DOCTYPE html>
            <aside id="rail">
                <textarea id="composer"></textarea>
                <div id="empty-target" tabindex="0"></div>
            </aside>
            """);

        await page.AddScriptTagAsync(new()
        {
            Type = "module",
            Content = $$"""
                {{talkScript.Replace("export function ", "function ", StringComparison.Ordinal)}}
                window.__closed = 0;
                const pageProxy = { invokeMethodAsync: (name) => { if (name === "CloseRailFromKeyboard") window.__closed++; } };
                attach(document.getElementById("rail"), pageProxy);
                """,
        });

        var outcome = await page.EvaluateAsync<string>("""
            () => {
                const rail = document.getElementById("rail");
                const composer = document.getElementById("composer");
                const empty = document.getElementById("empty-target");
                const fireEscape = (target, extra = {}) => {
                    const event = new KeyboardEvent("keydown", { key: "Escape", bubbles: true, cancelable: true, ...extra });
                    target.dispatchEvent(event);
                    return event.defaultPrevented;
                };

                // 1. IME composition must leave Escape alone even when field is empty.
                const composingPrevented = fireEscape(composer, { isComposing: true });
                const afterComposing = window.__closed;

                // 2. A text draft inside a rail field leaves Escape alone.
                composer.value = "Half-written side note";
                const draftPrevented = fireEscape(composer);
                const afterDraft = window.__closed;
                composer.value = "";

                // 3. An attached file inside the rail leaves Escape alone.
                const chip = document.createElement("div");
                chip.className = "dv-attached-file";
                rail.appendChild(chip);
                const attachmentPrevented = fireEscape(empty);
                const afterAttachment = window.__closed;
                chip.remove();

                // 4. Missing :popover-open support in closest must not throw SyntaxError and still closes the rail.
                const originalClosest = Element.prototype.closest;
                Element.prototype.closest = function (selector) {
                    if (selector.includes(":popover-open")) {
                        throw new DOMException("Invalid selector", "SyntaxError");
                    }
                    return originalClosest.call(this, selector);
                };
                try {
                    const plainPrevented = fireEscape(empty);
                    const afterPlain = window.__closed;
                    return JSON.stringify({
                        composingPrevented,
                        afterComposing,
                        draftPrevented,
                        afterDraft,
                        attachmentPrevented,
                        afterAttachment,
                        plainPrevented,
                        afterPlain
                    });
                } finally {
                    Element.prototype.closest = originalClosest;
                }
            }
            """);

        using var parsed = JsonDocument.Parse(outcome);
        var root = parsed.RootElement;
        root.GetProperty("composingPrevented").GetBoolean().Should().BeFalse();
        root.GetProperty("afterComposing").GetInt32().Should().Be(0, "Escape during IME composition must not close the rail");
        root.GetProperty("draftPrevented").GetBoolean().Should().BeFalse();
        root.GetProperty("afterDraft").GetInt32().Should().Be(0, "Escape inside a field with a draft must not close the rail");
        root.GetProperty("attachmentPrevented").GetBoolean().Should().BeFalse();
        root.GetProperty("afterAttachment").GetInt32().Should().Be(0, "Escape with an attached file in the rail must not close the rail");
        root.GetProperty("plainPrevented").GetBoolean().Should().BeTrue();
        root.GetProperty("afterPlain").GetInt32().Should().Be(1, "Escape with no draft must close the rail even without :popover-open");
    }

    public async ValueTask InitializeAsync()
    {
        talkScript = await File.ReadAllTextAsync(
            Path.Combine(RepositoryFiles.Root, "src", "Drover.Ui", "Pages", "Talk.razor.js"),
            Xunit.TestContext.Current.CancellationToken);
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new() { ExecutablePath = BrowserPath() });
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
    }

    #endregion

    #region Private Methods

    private static string BrowserPath() =>
        Environment.GetEnvironmentVariable("DROVER_TEST_CHROMIUM") is { Length: > 0 } chosen
            ? chosen
            : browserPaths.FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException("No Chromium executable found for TalkEscapeBrowserTests.");

    #endregion
}
