namespace Drover.Ui.Tests;

public sealed class GalleryTests : BunitContext
{
    #region Public Methods

    [Fact]
    public void The_gallery_is_not_shown_outside_development()
    {
        var page = RenderGallery(isDevelopment: false);

        page.Find("h1").TextContent.Should().Be("Not found");
        page.FindAll(".dv-gallery").Should().BeEmpty();
    }

    [Fact]
    public void The_gallery_says_its_content_is_sample_content()
    {
        var page = RenderGallery(isDevelopment: true);

        page.Find("h1").TextContent.Should().Be("Component gallery");
        page.Find(".dv-gallery-header").TextContent.Should().Contain("samples");
    }

    [Fact]
    public void The_gallery_shows_every_section_in_both_themes()
    {
        var page = RenderGallery(isDevelopment: true);

        foreach (var section in page.FindAll(".dv-gallery-section"))
        {
            section.QuerySelectorAll("[data-theme]").Select(panel => panel.GetAttribute("data-theme"))
                .Should().Equal("dark", "light");
        }
    }

    [Fact]
    public void The_gallery_loads_nothing_from_another_host()
    {
        var page = RenderGallery(isDevelopment: true);

        page.Markup.Should().NotContain("http://").And.NotContain("//cdn");
        page.FindAll("[src], [href]")
            .Select(element => element.GetAttribute("src") ?? element.GetAttribute("href"))
            .Should().OnlyContain(address => address!.StartsWith('/') || address.StartsWith("https://example.", StringComparison.Ordinal));
    }

    #endregion

    #region Private Methods

    private IRenderedComponent<Gallery> RenderGallery(bool isDevelopment)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddDroverUi();
        Services.AddSingleton(new UiEnvironment(isDevelopment));
        return Render<Gallery>();
    }

    #endregion
}
