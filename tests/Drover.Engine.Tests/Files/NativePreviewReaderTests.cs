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
    public async Task Refuses_pdf_when_the_parser_exceeds_its_per_stream_decode_budget()
    {
        var content = TestDocuments.PdfCommands(NativeFilePreview.MaxPdfDecodedStreamBytes + 1);
        var result = await Read(TestDocuments.ContentStreamPdf("/Filter /FlateDecode", [TestDocuments.CompressPdfCommands(content)]));

        result.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        result.Pages.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_pdf_when_separate_streams_exceed_the_shared_decode_budget()
    {
        var content = TestDocuments.PdfCommands(NativeFilePreview.MaxPdfDecodedStreamBytes);
        var compressed = TestDocuments.CompressPdfCommands(content);
        var result = await Read(TestDocuments.ContentStreamPdf("/Filter /FlateDecode", [compressed, compressed, compressed]));

        result.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        result.Pages.Should().BeNull();
    }

    [Theory]
    [InlineData("/F /Fl", "\n", "")]
    [InlineData("/Filter 5 0 R", "\n", "")]
    [InlineData("/Filter /FlateDecode", "\r", "")]
    [InlineData("/Filter /FlateDecode", "\n", " % dictionary comment\n")]
    public async Task Reads_small_flate_streams_using_the_parsers_filter_lookup(
        string filter, string streamNewline, string afterDictionary)
    {
        var compressed = TestDocuments.CompressPdfCommands(TestDocuments.PdfCommands(64));
        var result = await Read(TestDocuments.ContentStreamPdf(filter, [compressed], streamNewline, afterDictionary));

        result.Refusal.Should().BeNull();
        result.Pages.Should().ContainSingle().Which.Text.Should().Be("Safe content");
    }

    [Theory]
    [InlineData("/Filter /FlateDecode", "\n", "")]
    [InlineData("/F /Fl", "\n", "")]
    [InlineData("/Filter 5 0 R", "\n", "")]
    [InlineData("/Filter /FlateDecode", "\r", "")]
    [InlineData("/Filter /FlateDecode", "\n", " % dictionary comment\n")]
    public async Task Refuses_an_over_limit_flate_stream_however_the_filter_is_written(
        string filter, string streamNewline, string afterDictionary)
    {
        var compressed = TestDocuments.CompressPdfCommands(TestDocuments.PdfCommands(NativeFilePreview.MaxPdfDecodedStreamBytes + 1));
        var result = await Read(TestDocuments.ContentStreamPdf(filter, [compressed], streamNewline, afterDictionary));

        result.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        result.Pages.Should().BeNull();
    }

    [Fact]
    public async Task Refuses_an_over_limit_flate_stream_with_literal_stream_end_text_in_its_bytes()
    {
        var compressed = TestDocuments.FlateWithLiteralStreamEnd(
            TestDocuments.PdfCommands(NativeFilePreview.MaxPdfDecodedStreamBytes + 1));
        var pdf = TestDocuments.ContentStreamPdf("/Filter /FlateDecode", [compressed]);

        Encoding.Latin1.GetString(pdf).Should().Contain("% endstream");
        var result = await Read(pdf);

        result.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        result.Pages.Should().BeNull();
    }

    [Fact]
    public async Task Reads_a_small_flate_stream_with_literal_stream_end_text_in_its_bytes()
    {
        var compressed = TestDocuments.FlateWithLiteralStreamEnd(TestDocuments.PdfCommands(64));
        var result = await Read(TestDocuments.ContentStreamPdf("/Filter /FlateDecode", [compressed]));

        result.Refusal.Should().BeNull();
        result.Pages.Should().ContainSingle().Which.Text.Should().Be("Safe content");
    }

    [Theory]
    [InlineData("/Filter /LZWDecode", "lzw")]
    [InlineData("/Filter /ASCII85Decode", "ascii85")]
    [InlineData("/Filter [/ASCII85Decode /FlateDecode]", "ascii85-flate")]
    [InlineData("/Filter [/FlateDecode /FlateDecode]", "flate-flate")]
    public async Task Refuses_filters_and_filter_chains_a_preview_does_not_need(string filter, string encoding)
    {
        var commands = TestDocuments.PdfCommands(64);
        var stream = encoding switch
        {
            "lzw" => TestDocuments.Lzw(commands),
            "ascii85" => TestDocuments.Ascii85(commands),
            "ascii85-flate" => TestDocuments.Ascii85(TestDocuments.CompressPdfCommands(commands)),
            "flate-flate" => TestDocuments.CompressPdfCommands(TestDocuments.CompressPdfCommands(commands)),
            _ => TestDocuments.CompressPdfCommands(commands),
        };

        var result = await Read(TestDocuments.ContentStreamPdf(filter, [stream]));

        result.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
        result.Pages.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Reads_a_flate_stream_with_each_png_row_predictor(byte tag)
    {
        var predicted = TestDocuments.PngPredicted(TestDocuments.PdfCommands(64), 8, tag);
        var filter = "/Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 8 >>";
        var result = await Read(TestDocuments.ContentStreamPdf(filter, [TestDocuments.CompressPdfCommands(predicted)]));

        result.Refusal.Should().BeNull();
        result.Pages.Should().ContainSingle().Which.Text.Should().Be("Safe content");
    }

    [Fact]
    public async Task Reads_a_flate_stream_with_the_tiff_predictor()
    {
        var predicted = TestDocuments.TiffPredicted(TestDocuments.PdfCommands(64), 8);
        var filter = "/Filter /FlateDecode /DecodeParms << /Predictor 2 /Columns 8 >>";
        var result = await Read(TestDocuments.ContentStreamPdf(filter, [TestDocuments.CompressPdfCommands(predicted)]));

        result.Refusal.Should().BeNull();
        result.Pages.Should().ContainSingle().Which.Text.Should().Be("Safe content");
    }

    [Fact]
    public async Task Reads_a_pdf_whose_cross_reference_stream_uses_a_predictor()
    {
        var result = await Read(TestDocuments.XrefStreamPdf());

        result.Refusal.Should().BeNull();
        result.Pages.Should().ContainSingle().Which.Text.Should().Be("Safe content");
    }

    [Fact]
    public async Task Refuses_a_predicted_flate_stream_that_unpacks_past_the_per_stream_budget()
    {
        var raw = TestDocuments.PdfCommands(NativeFilePreview.MaxPdfDecodedStreamBytes);
        var predicted = TestDocuments.PngPredicted(raw, 1024, 2);
        var filter = "/Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 1024 >>";

        var result = await Read(TestDocuments.ContentStreamPdf(filter, [TestDocuments.CompressPdfCommands(predicted)]));

        result.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
        result.Pages.Should().BeNull();
    }

    [Theory]
    [InlineData("/Predictor 7 /Columns 8", NativePreviewRefusal.TooComplex)]
    [InlineData("/Predictor 12 /Columns 8 /Colors 0", NativePreviewRefusal.Malformed)]
    [InlineData("/Predictor 12 /Columns 8 /BitsPerComponent 3", NativePreviewRefusal.Malformed)]
    [InlineData("/Predictor 12 /Columns 6", NativePreviewRefusal.Malformed)]
    [InlineData("/Predictor 12 /Columns 2000000000", NativePreviewRefusal.Malformed)]
    [InlineData("/Predictor 2 /Columns 8 /BitsPerComponent 4", NativePreviewRefusal.TooComplex)]
    public async Task Refuses_a_predictor_it_cannot_decode_within_bounds(string parameters, NativePreviewRefusal expected)
    {
        var predicted = TestDocuments.PngPredicted(TestDocuments.PdfCommands(64), 8, 2);
        var filter = $"/Filter /FlateDecode /DecodeParms << {parameters} >>";

        var result = await Read(TestDocuments.ContentStreamPdf(filter, [TestDocuments.CompressPdfCommands(predicted)]));

        result.Refusal.Should().Be(expected);
        result.Pages.Should().BeNull();
    }

    [Fact]
    public async Task Reads_literal_stream_end_text_as_data_inside_a_stream()
    {
        var content = Encoding.ASCII.GetBytes("% a literal endstream in a comment\nBT /F1 12 Tf (Safe content) Tj ET\n");
        var result = await Read(TestDocuments.ContentStreamPdf(string.Empty, [content]));

        result.Refusal.Should().BeNull();
        result.Pages.Should().ContainSingle().Which.Text.Should().Be("Safe content");
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
    public async Task Gives_the_server_slot_back_when_a_parse_misses_its_deadline_though_the_parse_keeps_running()
    {
        var pdf = TestDocuments.Pdf("Page one");
        using var stuck = new ManualResetEventSlim(false);
        using var bothParsing = new Barrier(2);
        try
        {
            using var first = new MemoryStream(pdf);
            var missed = await NativePreviewReader.Read(
                first, CancellationToken.None, Guid.NewGuid(), TimeSpan.FromMilliseconds(40), () => stuck.Wait(TimeSpan.FromSeconds(10)));

            missed.Refusal.Should().Be(NativePreviewRefusal.TooComplex);

            var both = await Task.WhenAll(
                ReadWhileParsingTogether(pdf, bothParsing),
                ReadWhileParsingTogether(pdf, bothParsing));

            both.Should().OnlyContain(result => result.Refusal == null && result.Pages != null);
        }
        finally
        {
            stuck.Set();
            await WaitUntilPreviewsAreAccepted(pdf);
        }
    }

    [Fact]
    public async Task Starts_no_new_parse_while_two_parses_that_missed_their_deadline_are_still_running()
    {
        var pdf = TestDocuments.Pdf("Page one");
        using var stuck = new ManualResetEventSlim(false);
        var started = false;
        try
        {
            for (var run = 0; run < 2; run++)
            {
                using var content = new MemoryStream(pdf);
                var missed = await NativePreviewReader.Read(
                    content, CancellationToken.None, Guid.NewGuid(), TimeSpan.FromMilliseconds(40), () => stuck.Wait(TimeSpan.FromSeconds(10)));
                missed.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
            }

            using var third = new MemoryStream(pdf);
            var refused = await NativePreviewReader.Read(
                third, CancellationToken.None, Guid.NewGuid(), TimeSpan.FromSeconds(2), () => started = true);

            refused.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
            started.Should().BeFalse();
        }
        finally
        {
            stuck.Set();
            await WaitUntilPreviewsAreAccepted(pdf);
        }
    }

    [Fact]
    public async Task Keeps_one_users_slot_until_their_parse_that_missed_its_deadline_ends()
    {
        var pdf = TestDocuments.Pdf("Page one");
        var user = Guid.NewGuid();
        using var stuck = new ManualResetEventSlim(false);
        try
        {
            using var first = new MemoryStream(pdf);
            await NativePreviewReader.Read(
                first, CancellationToken.None, user, TimeSpan.FromMilliseconds(40), () => stuck.Wait(TimeSpan.FromSeconds(10)));

            using var second = new MemoryStream(pdf);
            var again = await NativePreviewReader.Read(second, CancellationToken.None, user, TimeSpan.FromSeconds(2));

            again.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
        }
        finally
        {
            stuck.Set();
            await WaitUntilPreviewsAreAccepted(pdf);
        }
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

    private static async Task<NativeFilePreview> ReadWhileParsingTogether(byte[] pdf, Barrier bothParsing)
    {
        using var content = new MemoryStream(pdf);
        var together = false;
        var result = await NativePreviewReader.Read(
            content,
            CancellationToken.None,
            Guid.NewGuid(),
            TimeSpan.FromSeconds(10),
            () => together = bothParsing.SignalAndWait(TimeSpan.FromSeconds(3)));

        return together ? result : new(Refusal: NativePreviewRefusal.TooComplex);
    }

    private static async Task WaitUntilPreviewsAreAccepted(byte[] pdf)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            using var content = new MemoryStream(pdf);
            var result = await NativePreviewReader.Read(content, CancellationToken.None, Guid.NewGuid());
            if (result.Refusal is null)
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private static async Task<NativeFilePreview> Read(byte[] bytes)
    {
        using var content = new MemoryStream(bytes);
        return await NativePreviewReader.Read(content, CancellationToken.None);
    }

    #endregion
}
