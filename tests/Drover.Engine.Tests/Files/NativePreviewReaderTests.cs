namespace Drover.Engine.Tests.Files;

/// <summary>
/// Bounded inspection and parsing of stored file bytes for the native client's Markdown, static image, PDF, and
/// diagram viewers.
/// </summary>
public sealed class NativePreviewReaderTests
{
    #region Public Methods

    [Fact]
    public async Task Selects_markdown_from_utf8_bytes()
    {
        var result = await Read([.. "# Heading"u8]);

        result.Markdown.Should().Be("# Heading");
        result.Refusal.Should().BeNull();
    }

    [Fact]
    public async Task Parses_standalone_flowchart_into_bounded_nodes_and_edges()
    {
        var result = await Read([.. "flowchart TD\nA[\"Upload bytes\"] -->|\"inspect\"| B[\"Render natively\"]"u8]);

        result.Refusal.Should().BeNull();
        result.Diagram.Should().NotBeNull();
        result.Diagram!.Nodes.Should().Equal(
            new NativeDiagramNode("A", "Upload bytes"),
            new NativeDiagramNode("B", "Render natively"));
        result.Diagram.Edges.Should().Equal(
            new NativeDiagramEdge("A", "B", "inspect"));
    }

    [Fact]
    public async Task Refuses_flowchart_with_click_or_external_link_directives()
    {
        var result = await Read([.. "flowchart TD\nA --> B\nclick A \"https://example.test/steal\""u8]);

        result.Refusal.Should().Be(NativePreviewRefusal.Malformed);
        result.Diagram.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_flowchart_past_node_limit()
    {
        var lines = new List<string> { "flowchart TD" };
        for (var index = 0; index <= NativeFilePreview.MaxDiagramNodes; index++)
        {
            lines.Add($"N{index}[\"Node {index}\"] --> N{index + 1}[\"Node {index + 1}\"]");
        }

        var result = await Read(Encoding.UTF8.GetBytes(string.Join('\n', lines)));

        result.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
        result.Diagram.Should().BeNull();
    }

    [Fact]
    public void Refuses_diagram_text_past_character_and_byte_limit_before_splitting_lines()
    {
        var oversized = $"flowchart TD\nA --> B\n{new string('x', NativeFilePreview.MaxTextBytes + 1)}";

        var parsed = NativeDiagramParser.TryParse(oversized, out var diagram, out var refusal);

        parsed.Should().BeFalse();
        diagram.Should().BeNull();
        refusal.Should().Be(NativePreviewRefusal.TooLarge);
    }

    [Fact]
    public async Task Extracts_pdf_pages_within_page_and_character_limits()
    {
        var pdf = TestDocuments.Pdf("First page text", "Second page text");

        var result = await Read(pdf);

        result.Refusal.Should().BeNull();
        result.Pages.Should().NotBeNull();
        result.Pages!.Should().HaveCount(2);
        result.Pages[0].Number.Should().Be(1);
        result.Pages[0].Text.Should().Contain("First page text");
        result.Pages[1].Number.Should().Be(2);
        result.Pages[1].Text.Should().Contain("Second page text");
    }

    [Fact]
    public async Task Extracts_scanned_pdf_page_images_when_dimensions_fit()
    {
        var first = TestDocuments.Jpeg(20, 30, 0x01);
        var second = TestDocuments.Jpeg(20, 30, 0x02);
        var pdf = TestDocuments.ScannedPdf(first, second);

        var result = await Read(pdf);

        result.Refusal.Should().BeNull();
        result.Pages.Should().NotBeNull();
        result.Pages!.Should().HaveCount(2);
        result.Pages[0].Image.Should().Equal(first);
        result.Pages[1].Image.Should().Equal(second);
    }

    [Fact]
    public async Task Refuses_pdf_with_more_pages_than_preview_cap()
    {
        var pages = Enumerable.Range(1, NativeFilePreview.MaxPdfPages + 1).Select(index => $"Page {index}").ToArray();
        var pdf = TestDocuments.Pdf(pages);

        var result = await Read(pdf);

        result.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        result.Pages.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_damaged_pdf_bytes()
    {
        var result = await Read([.. "%PDF-1.7\ninvalid"u8]);

        result.Refusal.Should().Be(NativePreviewRefusal.Malformed);
        result.Pages.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_pdf_flate_bomb_before_full_stream_inflation()
    {
        var inflated = new byte[NativeFilePreview.MaxPdfDecodedStreamBytes + 4096];
        using var compressedStream = new MemoryStream();
        await using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await zlib.WriteAsync(inflated, TestContext.Current.CancellationToken);
        }

        var compressed = compressedStream.ToArray();
        compressed.Length.Should().BeLessThan(NativeFilePreview.MaxPdfBytes);

        using var pdf = new MemoryStream();
        var header = Encoding.ASCII.GetBytes(
            $"%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Count 1 /Kids [3 0 R] >> endobj\n3 0 obj << /Type /Page /Parent 2 0 R /Contents 4 0 R >> endobj\n4 0 obj << /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
        pdf.Write(header);
        pdf.Write(compressed);
        pdf.Write("\nendstream\nendobj\n%%EOF"u8);

        var result = await Read(pdf.ToArray());

        result.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        result.Pages.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_pdf_with_cyclic_or_fan_out_page_tree()
    {
        var cyclic = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Count 1 /Kids [3 0 R] /Parent 3 0 R >> endobj\n3 0 obj << /Type /Pages /Count 1 /Kids [2 0 R] /Parent 2 0 R >> endobj\n%%EOF");
        var cyclicResult = await Read(cyclic);

        cyclicResult.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
        cyclicResult.Pages.Should().BeNull();

        var fanOut = Encoding.ASCII.GetBytes(
            "%PDF-1.7\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n2 0 obj << /Type /Pages /Count 2 /Kids [3 0 R 3 0 R] >> endobj\n3 0 obj << /Type /Page /Parent 2 0 R >> endobj\n%%EOF");
        var fanOutResult = await Read(fanOut);

        fanOutResult.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
        fanOutResult.Pages.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_pdf_that_exceeds_parse_deadline()
    {
        var pdf = TestDocuments.Pdf("Page one");
        using var gate = new ManualResetEventSlim(false);

        using var content = new MemoryStream(pdf);
        var result = await NativePreviewReader.Read(
            content,
            CancellationToken.None,
            Guid.NewGuid(),
            TimeSpan.FromMilliseconds(40),
            () => gate.Wait(TimeSpan.FromMilliseconds(250)));
        gate.Set();

        result.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
        result.Pages.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_concurrent_pdf_previews_from_same_user_or_beyond_server_gate()
    {
        var pdf = TestDocuments.Pdf("Concurrent page");
        var sameUser = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim(false);

        var firstTask = Task.Run(async () =>
        {
            using var firstStream = new MemoryStream(pdf);
            return await NativePreviewReader.Read(
                firstStream,
                CancellationToken.None,
                sameUser,
                TimeSpan.FromSeconds(2),
                () =>
                {
                    entered.TrySetResult();
                    release.Wait(TimeSpan.FromSeconds(2));
                });
        });

        await entered.Task;

        using var secondStream = new MemoryStream(pdf);
        var duplicateUserResult = await NativePreviewReader.Read(
            secondStream,
            CancellationToken.None,
            sameUser,
            TimeSpan.FromMilliseconds(100));

        duplicateUserResult.Refusal.Should().Be(NativePreviewRefusal.TooComplex);

        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSlotTask = Task.Run(async () =>
        {
            using var slotStream = new MemoryStream(pdf);
            return await NativePreviewReader.Read(
                slotStream,
                CancellationToken.None,
                Guid.NewGuid(),
                TimeSpan.FromSeconds(2),
                () =>
                {
                    secondEntered.TrySetResult();
                    release.Wait(TimeSpan.FromSeconds(2));
                });
        });

        await secondEntered.Task;

        using var thirdStream = new MemoryStream(pdf);
        var overGateResult = await NativePreviewReader.Read(
            thirdStream,
            CancellationToken.None,
            Guid.NewGuid(),
            TimeSpan.FromMilliseconds(60));

        overGateResult.Refusal.Should().Be(NativePreviewRefusal.TooComplex);

        release.Set();
        var firstResult = await firstTask;
        var secondSlotResult = await secondSlotTask;

        firstResult.Refusal.Should().BeNull();
        secondSlotResult.Refusal.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_invalid_utf8()
    {
        var result = await Read([0xC3, 0x28]);

        result.Refusal.Should().Be(NativePreviewRefusal.Malformed);
    }

    [Fact]
    public async Task Refuses_text_past_byte_limit()
    {
        var bytes = new byte[NativeFilePreview.MaxTextBytes + 1];
        Array.Fill(bytes, (byte)'a');

        var result = await Read(bytes);

        result.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
    }

    [Fact]
    public async Task Refuses_truncated_image_header()
    {
        var result = await Read([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        result.Refusal.Should().Be(NativePreviewRefusal.Malformed);
    }

    [Fact]
    public async Task Refuses_small_compressed_input_with_large_pixel_dimensions()
    {
        byte[] header =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13,
            .. "IHDR"u8, 0, 0, 0x10, 0, 0, 0, 0x10, 0,
        ];

        var result = await Read(header);

        result.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        result.Image.Should().BeNull();
    }

    #endregion

    #region Private Methods

    private static async Task<NativeFilePreview> Read(byte[] bytes)
    {
        using var content = new MemoryStream(bytes);
        return await NativePreviewReader.Read(content, CancellationToken.None);
    }

    #endregion
}
