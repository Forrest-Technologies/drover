namespace Drover.Presentation.Tests.Rules;

/// <summary>
/// Markdown files parsed for native preview keep raw HTML inert as text, convert remote images into visible external
/// links, and refuse excessive node count or depth.
/// </summary>
public sealed class FileMarkdownTests
{
    #region Public Methods

    [Fact]
    public void Raw_html_remains_text()
    {
        var nodes = FileMarkdown.Parse("<script src=\"https://example.test/run.js\"></script>");

        var paragraph = nodes!.OfType<ElementNode>().Should().ContainSingle().Subject;
        paragraph.Children.Should().ContainSingle().Subject.Should().BeOfType<TextNode>()
            .Which.Text.Should().Contain("<script");
    }

    [Fact]
    public void Remote_images_become_visible_external_addresses()
    {
        const string Address = "https://example.test/tracker.png";
        var nodes = FileMarkdown.Parse($"**![alt text]({Address})**");

        var paragraph = nodes!.OfType<ElementNode>().Should().ContainSingle().Subject;
        var strong = paragraph.Children.Should().ContainSingle().Subject.Should().BeOfType<ElementNode>().Subject;
        var link = strong.Children.Should().ContainSingle().Subject.Should().BeOfType<LinkNode>().Subject;
        link.Address.Should().Be(Address);
        link.Children.Should().ContainSingle().Subject.Should().Be(new TextNode(Address));
    }

    [Fact]
    public void Refuses_excessive_node_count()
    {
        var text = string.Concat(Enumerable.Repeat("# Heading\n\n", 300));

        FileMarkdown.Parse(text).Should().BeNull();
    }

    #endregion
}
