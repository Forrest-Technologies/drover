namespace Drover.Presentation.Tests.ViewModels.Chat;

/// <summary>
/// Opening, paging through, refusing, and closing native file previews in <see cref="NativeFilePreviewViewModel"/>.
/// </summary>
public sealed class NativeFilePreviewViewModelTests
{
    #region Public Methods

    [Fact]
    public void Bot_html_file_is_handled_by_isolated_html_preview_and_not_native_file_preview()
    {
        var model = new NativeFilePreviewViewModel
        {
            File = MakeFile("report.html", "text/html", fromBot: true),
        };

        model.IsSupported.Should().BeFalse();
    }

    [Fact]
    public async Task Opens_markdown_preview_with_remote_image_converted_to_visible_link_and_releases_on_close()
    {
        var model = new NativeFilePreviewViewModel
        {
            File = MakeFile("notes.md", "text/markdown", fromBot: true),
            ReadPreview = (_, _) => Task.FromResult(new NativeFilePreview(
                Markdown: "# Heading\n\n![diagram](https://example.test/pic.png)")),
        };

        await model.Toggle();

        model.Open.Should().BeTrue();
        model.Status.Should().BeNull();
        model.MarkdownNodes.OfType<ElementNode>().Should().HaveCount(2);

        await model.Toggle();

        model.Open.Should().BeFalse();
        model.MarkdownNodes.Should().BeEmpty();
    }

    [Fact]
    public async Task Steps_through_pdf_pages_and_clears_pages_on_close()
    {
        var model = new NativeFilePreviewViewModel
        {
            File = MakeFile("manual.pdf", "application/pdf", fromBot: true),
            ReadPreview = (_, _) => Task.FromResult(new NativeFilePreview(
                Pages:
                [
                    new NativePdfPage(1, "First page"),
                    new NativePdfPage(2, "Second page"),
                ])),
        };

        await model.Toggle();

        model.Open.Should().BeTrue();
        model.CurrentPdfPage!.Number.Should().Be(1);
        model.PageCaption.Should().Be("Page 1 of 2");
        model.CanPreviousPage.Should().BeFalse();
        model.CanNextPage.Should().BeTrue();

        model.NextPage();

        model.CurrentPdfPage!.Number.Should().Be(2);
        model.PageCaption.Should().Be("Page 2 of 2");
        model.CanPreviousPage.Should().BeTrue();
        model.CanNextPage.Should().BeFalse();

        model.PreviousPage();

        model.CurrentPdfPage!.Number.Should().Be(1);

        model.Close();

        model.Open.Should().BeFalse();
        model.PdfPages.Should().BeEmpty();
        model.CurrentPdfPage.Should().BeNull();
    }

    [Fact]
    public async Task Opens_flowchart_diagram_and_releases_on_file_replacement()
    {
        var diagram = new NativeDiagram(
            "TD",
            [new NativeDiagramNode("A", "Start"), new NativeDiagramNode("B", "Finish")],
            [new NativeDiagramEdge("A", "B", "go")]);
        var model = new NativeFilePreviewViewModel
        {
            File = MakeFile("flow.mmd", "text/plain", fromBot: true),
            ReadPreview = (_, _) => Task.FromResult(new NativeFilePreview(Diagram: diagram)),
        };

        await model.Toggle();

        model.Diagram.Should().Be(diagram);

        model.File = MakeFile("other.txt", "text/plain", fromBot: false);

        model.Open.Should().BeFalse();
        model.Diagram.Should().BeNull();
    }

    [Theory]
    [InlineData(NativePreviewRefusal.TooLarge, "16 KiB")]
    [InlineData(NativePreviewRefusal.Malformed, "invalid preview data")]
    [InlineData(NativePreviewRefusal.Unsupported, "no native preview")]
    [InlineData(NativePreviewRefusal.TooComplex, "complexity limit")]
    public async Task Shows_clear_refusal_sentence_when_file_is_refused(NativePreviewRefusal refusal, string expectedPhrase)
    {
        var model = new NativeFilePreviewViewModel
        {
            File = MakeFile("bad.bin", "application/octet-stream", fromBot: true),
            ReadPreview = (_, _) => Task.FromResult(new NativeFilePreview(Refusal: refusal)),
        };

        await model.Toggle();

        model.Open.Should().BeTrue();
        model.Status.Should().Contain(expectedPhrase).And.Contain("Download");
    }

    #endregion

    #region Private Methods

    private static SharedFile MakeFile(string name, string contentType, bool fromBot) =>
        new(
            Guid.NewGuid(),
            name,
            contentType,
            128,
            "https://drover.test/api/spaces/00000000-0000-0000-0000-000000000001/files/00000000-0000-0000-0000-000000000002",
            fromBot,
            DateTimeOffset.UtcNow);

    #endregion
}
