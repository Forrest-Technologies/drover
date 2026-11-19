namespace Drover.Presentation.Tests.Rules;

/// <summary>The browser containment boundary for untrusted bot HTML.</summary>
public sealed class HtmlPreviewRulesTests
{
    #region Public Methods

    [Fact]
    public void The_browser_preview_is_an_empty_capability_iframe()
    {
        var source = NativeSource("Chat", "HtmlPreview.cs");

        source.Should().Contain("CreateHtmlElement(\"iframe\")");
        source.Should().Contain("(\"sandbox\", string.Empty)");
        source.Should().Contain("(\"credentialless\", string.Empty)");
        source.Should().Contain("(\"referrerpolicy\", \"no-referrer\")");
        source.Should().Contain("(\"allow\", string.Empty)");
        source.Should().NotContain("allow-same-origin");
        source.Should().NotContain("allow-scripts");
        source.Should().NotContain("allow-forms");
        source.Should().NotContain("allow-popups");
        source.Should().NotContain("allow-top-navigation");
        source.Should().NotContain("allow-downloads");
        source.Should().NotContain("SetHtmlContent");
    }

    [Fact]
    public void A_native_head_refuses_before_requesting_a_preview_grant()
    {
        var source = NativeSource("Chat", "FileCard.cs");
        var browserGate = source.IndexOf("if (OperatingSystem.IsBrowser())", StringComparison.Ordinal);
        var previewRequest = source.IndexOf("await model.Preview()", browserGate, StringComparison.Ordinal);
        var refusal = source.IndexOf("model.RefusePreview()", browserGate, StringComparison.Ordinal);

        browserGate.Should().BePositive();
        previewRequest.Should().BeGreaterThan(browserGate);
        refusal.Should().BeGreaterThan(previewRequest);
    }

    #endregion

    #region Private Methods

    private static string NativeSource(params string[] path) =>
        File.ReadAllText(Path.Combine([Repository.NativeFolder, .. path]));

    #endregion
}
