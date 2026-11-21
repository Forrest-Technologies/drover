namespace Drover.Engine.Tests.Files;

/// <summary>Reading text out of each kind of document, within the limits, and refusing what cannot be read.</summary>
public sealed class DocumentReaderTests
{
    #region Private Fields

    /// <summary>The most one Flate stream may unpack to when a document is read.</summary>
    private const int ReadingStreamBytes = 64 * 1024 * 1024;

    /// <summary>Group 4 data for eight all white rows: one bit each, then the end of block marker.</summary>
    private static readonly byte[] faxOfEightWhiteRows = [0xFF, 0x00, 0x10, 0x01];

    private static readonly DocumentLimits roomy = new(MaxPages: 50, MaxCharacters: 10_000, MaxPageImages: 10);

    #endregion

    #region Public Methods

    [Fact]
    public void A_pdf_is_read_page_by_page()
    {
        var read = DocumentReader.Read(TestDocuments.Pdf("Invoice 4471", "Total due 1,250.00"), DocumentTypes.Pdf, roomy);

        read.Text.Should().Be("[Page 1]\nInvoice 4471\n\n[Page 2]\nTotal due 1,250.00");
        (read.Unit, read.Read, read.Total, read.TextCut, read.PagesCut).Should().Be(("pages", 2, 2, false, false));
        read.PageImages.Should().BeEmpty();
    }

    [Fact]
    public void A_pdf_longer_than_the_page_limit_is_read_to_the_limit()
    {
        var read = DocumentReader.Read(TestDocuments.Pdf("one", "two", "three"), DocumentTypes.Pdf, roomy with { MaxPages = 2 });

        read.Text.Should().Contain("two").And.NotContain("three");
        (read.Read, read.Total, read.PagesCut).Should().Be((2, 3, true));
    }

    [Fact]
    public void Text_past_the_character_limit_is_cut_and_reading_stops()
    {
        var read = DocumentReader.Read(TestDocuments.Pdf("aaaaaaaaaa", "bbbbbbbbbb", "cccccccccc"), DocumentTypes.Pdf, roomy with { MaxCharacters = 25 });

        read.Text.Length.Should().BeLessThanOrEqualTo(25);
        read.Text.Should().NotContain("c");
        read.TextCut.Should().BeTrue();
        read.Read.Should().Be(2, "the page that did not fit was the last one read");
    }

    [Fact]
    public void A_scanned_pdf_has_no_text_and_gives_the_picture_on_each_page_as_it_is_stored()
    {
        var first = TestDocuments.Jpeg(20, 30, 0x01);
        var second = TestDocuments.Jpeg(20, 30, 0x02);

        var read = DocumentReader.Read(TestDocuments.ScannedPdf(first, second), DocumentTypes.Pdf, roomy);

        read.HasText.Should().BeFalse();
        read.PageImages.Select(image => image.MediaType).Should().Equal("image/jpeg", "image/jpeg");
        read.PageImages.Select(image => image.Data.ToArray()).Should().BeEquivalentTo([first, second], options => options.WithStrictOrdering());
    }

    [Fact]
    public void A_scanned_pdf_gives_no_more_pictures_than_asked_for_and_none_when_none_are()
    {
        var pdf = TestDocuments.ScannedPdf(TestDocuments.Jpeg(4, 4, 1), TestDocuments.Jpeg(4, 4, 2), TestDocuments.Jpeg(4, 4, 3));

        DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy with { MaxPageImages = 2 }).PageImages.Should().HaveCount(2);
        DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy with { MaxPageImages = 0 }).PageImages.Should().BeEmpty();
    }

    [Fact]
    public void Scanned_pdf_pictures_stop_before_crossing_combined_decoded_byte_bounds()
    {
        var pdf = TestDocuments.ScannedPdf(
            TestDocuments.Jpeg(2_048, 2_048, 1),
            TestDocuments.Jpeg(2_048, 2_048, 2));
        var limits = roomy with
        {
            MaxDecodedImageBytes = 16L * 1024 * 1024,
            SingleImageDecodedCap = 64L * 1024 * 1024,
        };

        var read = DocumentReader.Read(pdf, DocumentTypes.Pdf, limits);

        read.PageImages.Should().ContainSingle();
        read.PageImageDecodedBytes.Should().Be(16L * 1024 * 1024);
        read.ImagesRejected.Should().BeTrue();
        read.ImagesExceedSingleLimits.Should().BeFalse();
    }

    [Fact]
    public void Scanned_pdf_pictures_over_the_dimension_bound_are_rejected_before_extraction()
    {
        var pdf = TestDocuments.ScannedPdf(TestDocuments.Jpeg(4_097, 4, 1));

        var read = DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy with { MaxImageDimension = 4_096 });

        read.PageImages.Should().BeEmpty();
        read.ImagesRejected.Should().BeTrue();
        read.ImagesExceedSingleLimits.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scanned_pdf_picture_headers_must_be_readable_and_match_the_pdf_dictionary(bool zeroHeaderHeight)
    {
        var pdf = TestDocuments.ScannedPdfWithDeclaredImageSize(
            actualWidth: 8_000,
            actualHeight: 6_000,
            declaredWidth: 1_200,
            declaredHeight: 1_600,
            zeroHeaderHeight: zeroHeaderHeight);

        var read = DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy);

        read.PageImages.Should().BeEmpty();
        read.ImagesRejected.Should().BeTrue();
        read.ImagesExceedSingleLimits.Should().BeTrue();
    }

    [Fact]
    public void A_word_document_is_read_paragraph_by_paragraph()
    {
        var read = DocumentReader.Read(TestDocuments.Docx("Lease agreement", "Rent is due on the first."), DocumentTypes.Word, roomy);

        read.Text.Should().Be("Lease agreement\nRent is due on the first.");
        (read.Unit, read.PagesCut, read.TextCut).Should().Be((null, false, false));
    }

    [Fact]
    public void A_workbook_is_read_sheet_by_sheet_with_shared_inline_and_number_cells()
    {
        var xlsx = TestDocuments.Xlsx(("Budget", [["Rent", "1200"], ["Food", "450.5"]]), ("Notes", [["Paid in March"]]));

        var read = DocumentReader.Read(xlsx, DocumentTypes.Excel, roomy);

        read.Text.Should().Be("[Sheet Budget]\nShared heading\nRent\t1200\nFood\t450.5\n\n[Sheet Notes]\nShared heading\nPaid in March");
        (read.Unit, read.Read, read.Total).Should().Be(("sheets", 2, 2));
    }

    [Fact]
    public void A_workbook_with_more_sheets_than_the_page_limit_is_read_to_the_limit()
    {
        var xlsx = TestDocuments.Xlsx(("One", [["a"]]), ("Two", [["b"]]), ("Three", [["c"]]));

        var read = DocumentReader.Read(xlsx, DocumentTypes.Excel, roomy with { MaxPages = 1 });

        read.Text.Should().Contain("[Sheet One]").And.NotContain("Two");
        (read.Read, read.Total, read.PagesCut).Should().Be((1, 3, true));
    }

    [Fact]
    public void A_presentation_is_read_slide_by_slide()
    {
        var read = DocumentReader.Read(TestDocuments.Pptx("Quarterly review", "Sales grew 8%"), DocumentTypes.PowerPoint, roomy);

        read.Text.Should().Be("[Slide 1]\nQuarterly review\n\n[Slide 2]\nSales grew 8%");
        (read.Unit, read.Read, read.Total).Should().Be(("slides", 2, 2));
    }

    [Theory]
    [InlineData(DocumentTypes.Pdf)]
    [InlineData(DocumentTypes.Word)]
    [InlineData(DocumentTypes.Excel)]
    [InlineData(DocumentTypes.PowerPoint)]
    public void A_damaged_document_is_reported_as_unreadable_in_a_sentence(string contentType)
    {
        byte[] damaged = [.. "%PDF-1.7\n"u8, 0x50, 0x4B, 0x03, 0x04, 0, 1, 2, 3];

        var reading = () => DocumentReader.Read(damaged, contentType, roomy);

        reading.Should().Throw<DocumentUnreadableException>().WithMessage("it is damaged, or not the kind of file its name says");
    }

    [Fact]
    public void A_pdf_stream_that_unpacks_past_the_reading_budget_is_unreadable_not_unpacked()
    {
        var bomb = TestDocuments.CompressPdfCommands(TestDocuments.PdfCommands(ReadingStreamBytes + 1));
        var pdf = TestDocuments.ContentStreamPdf("/Filter /FlateDecode", [bomb]);
        pdf.Length.Should().BeLessThan(1024 * 1024);

        var reading = () => DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy);

        reading.Should().Throw<DocumentUnreadableException>().WithMessage("it unpacks to more than the server will read");
    }

    [Fact]
    public void A_pdf_whose_streams_together_unpack_past_the_reading_budget_is_unreadable()
    {
        var raw = TestDocuments.CompressPdfCommands(TestDocuments.PdfCommands(ReadingStreamBytes));
        var pdf = TestDocuments.ContentStreamPdf("/Filter /FlateDecode", [raw, raw, raw]);

        var reading = () => DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy);

        reading.Should().Throw<DocumentUnreadableException>().WithMessage("it unpacks to more than the server will read");
    }

    [Fact]
    public void A_run_length_stream_that_could_unpack_past_the_reading_budget_is_unreadable_before_it_is_unpacked()
    {
        var pdf = TestDocuments.ContentStreamPdf("/Filter /RunLengthDecode", [TestDocuments.RunLengthRuns(1024 * 1024)]);

        var reading = () => DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy);

        reading.Should().Throw<DocumentUnreadableException>().WithMessage("it unpacks to more than the server will read");
    }

    [Fact]
    public void A_ccitt_stream_that_names_a_picture_past_the_reading_budget_is_unreadable_before_it_is_unpacked()
    {
        var filter = "/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 100000 /Rows 100000 >>";
        var pdf = TestDocuments.ContentStreamPdf(filter, [[1, 2, 3, 4]]);

        var reading = () => DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy);

        reading.Should().Throw<DocumentUnreadableException>().WithMessage("it unpacks to more than the server will read");
    }

    [Theory]
    [InlineData("/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 8 >> /Height 8")]
    [InlineData("/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 8 /Rows 0 >> /Height 8")]
    [InlineData("/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 8 >> /H 8")]
    [InlineData("/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 100000 /Rows 100000 >> /Height 8")]
    public void A_small_fax_stream_is_read_when_its_height_is_on_the_image_or_wins_over_the_row_count(string filter)
    {
        var pdf = TestDocuments.ContentStreamPdf(filter, [faxOfEightWhiteRows]);

        var read = DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy);

        read.Read.Should().Be(1);
    }

    [Theory]
    [InlineData("/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 100000 >> /Height 100000")]
    [InlineData("/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 100000 >> /H 100000")]
    [InlineData("/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 100000 /Rows 100000 >>")]
    [InlineData("/Filter /CCITTFaxDecode /DecodeParms << /K -1 /Columns 100000 /Rows 8 >> /Height 100000")]
    public void A_fax_stream_whose_size_the_decoder_would_allocate_past_the_budget_is_unreadable_however_it_is_named(string filter)
    {
        var pdf = TestDocuments.ContentStreamPdf(filter, [faxOfEightWhiteRows]);

        var reading = () => DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy);

        reading.Should().Throw<DocumentUnreadableException>().WithMessage("it unpacks to more than the server will read");
    }

    [Fact]
    public void An_ascii85_stream_that_could_unpack_past_the_reading_budget_is_unreadable()
    {
        var pdf = TestDocuments.ContentStreamPdf(
            "/Filter /ASCII85Decode", [[.. Enumerable.Repeat((byte)'z', (ReadingStreamBytes / 4) + 1), .. "~>"u8]]);

        var reading = () => DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy);

        reading.Should().Throw<DocumentUnreadableException>().WithMessage("it unpacks to more than the server will read");
    }

    [Fact]
    public void A_run_length_stream_that_stays_small_is_read_though_its_worst_case_is_large()
    {
        var pdf = TestDocuments.ContentStreamPdf(
            "/Filter /RunLengthDecode",
            [TestDocuments.RunLengthLiterals(300_000), TestDocuments.RunLengthLiterals(300_000), TestDocuments.RunLengthLiterals(300_000)]);

        var read = DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy);

        read.Read.Should().Be(1);
    }

    [Fact]
    public void A_pdf_stream_with_a_chain_of_filters_is_still_read()
    {
        var packed = TestDocuments.Ascii85(TestDocuments.CompressPdfCommands(TestDocuments.PdfCommands(64)));
        var pdf = TestDocuments.ContentStreamPdf("/Filter [/ASCII85Decode /FlateDecode]", [packed]);

        DocumentReader.Read(pdf, DocumentTypes.Pdf, roomy).Text.Should().Contain("Safe content");
    }

    [Fact]
    public void A_pdf_with_a_predicted_cross_reference_stream_is_still_read()
    {
        DocumentReader.Read(TestDocuments.XrefStreamPdf(), DocumentTypes.Pdf, roomy).Text.Should().Contain("Safe content");
    }

    [Fact]
    public void A_word_document_is_read_as_the_kind_its_type_names_so_a_workbook_named_as_one_is_unreadable()
    {
        var reading = () => DocumentReader.Read(TestDocuments.Xlsx(("One", [["a"]])), DocumentTypes.Word, roomy);

        reading.Should().Throw<DocumentUnreadableException>();
    }

    #endregion
}
