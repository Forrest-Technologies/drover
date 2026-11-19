namespace Drover.Domain.Tests;

/// <summary>What the server decides a file is, from its bytes alone.</summary>
public sealed class FileInspectorTests
{
    #region Tests

    [Fact]
    public void A_png_is_a_picture_with_the_size_its_header_gives() =>
        Inspect(Png(640, 480), "shot.txt").Should().Be(new FileFacts(FileKind.Image, "image/png", 640, 480));

    [Fact]
    public void A_jpeg_is_a_picture_with_the_size_of_its_first_frame_after_other_segments() =>
        Inspect(Jpeg(1920, 1080), "photo.jpg").Should().Be(new FileFacts(FileKind.Image, "image/jpeg", 1920, 1080));

    [Fact]
    public void A_gif_is_a_picture() =>
        Inspect([.. "GIF89a"u8, 0x20, 0x01, 0x10, 0x00, 0, 0, 0], "a.gif").Should().Be(new FileFacts(FileKind.Image, "image/gif", 288, 16));

    [Theory]
    [InlineData("VP8X")]
    [InlineData("VP8L")]
    [InlineData("VP8 ")]
    public void A_webp_of_each_form_is_a_picture(string form) =>
        Inspect(Webp(form, 300, 200), "a.webp").Should().Be(new FileFacts(FileKind.Image, "image/webp", 300, 200));

    [Fact]
    public void A_picture_header_without_a_size_it_can_read_is_not_a_picture()
    {
        byte[] truncated = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0];

        Inspect(truncated, "x.png").Kind.Should().Be(FileKind.Other);
    }

    [Fact]
    public void A_picture_larger_than_any_screen_shows_is_not_shown_as_a_picture() =>
        Inspect(Png(40_000, 10), "wide.png").Kind.Should().Be(FileKind.Other);

    [Fact]
    public void Utf8_text_is_text_with_its_first_lines()
    {
        var facts = Inspect(Encoding.UTF8.GetBytes("one\r\ntwo\nthree\nfour\nfive\nsix\nseven\neight"), "notes.txt");

        facts.Should().Be(new FileFacts(FileKind.Text, "text/plain", Preview: "one\ntwo\nthree\nfour\nfive\nsix"));
    }

    [Theory]
    [InlineData("data.json", "application/json")]
    [InlineData("table.csv", "text/csv")]
    [InlineData("README.md", "text/markdown")]
    [InlineData("page.html", "text/html")]
    [InlineData("drawing.svg", "text/plain")]
    [InlineData("noextension", "text/plain")]
    public void The_name_only_tells_kinds_of_text_apart(string name, string contentType)
    {
        var facts = Inspect("{\"a\": 1}"u8.ToArray(), name);

        facts.Kind.Should().Be(FileKind.Text);
        facts.ContentType.Should().Be(contentType);
        FileInspector.KindOf(facts.ContentType).Should().Be(FileKind.Text);
    }

    [Fact]
    public void A_character_split_across_two_chunks_is_still_text()
    {
        var bytes = Encoding.UTF8.GetBytes("café ✓ done");
        var inspector = new FileInspector();
        var split = Array.IndexOf(bytes, (byte)0xE2) + 1;

        inspector.Append(bytes.AsSpan(0, split));
        inspector.Append(bytes.AsSpan(split));

        inspector.Finish("a.txt").Kind.Should().Be(FileKind.Text);
    }

    [Fact]
    public void Bytes_that_are_not_utf8_anywhere_in_the_file_make_it_another_kind()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes(new string('a', 300_000)), 0xC3, 0x28];

        Inspect(bytes, "late.txt").Should().Be(new FileFacts(FileKind.Other, "application/octet-stream"));
    }

    [Fact]
    public void A_nul_byte_makes_it_another_kind_whatever_the_name_says() =>
        Inspect([0x25, 0x50, 0x44, 0x46, 0x00, 0x01], "report.pdf").Should().Be(new FileFacts(FileKind.Other, "application/octet-stream"));

    [Theory]
    [InlineData("report.pdf")]
    [InlineData("report.bin")]
    public void A_pdf_is_a_document_by_its_header_whatever_its_name(string name) =>
        Inspect([.. "%PDF-1.7\n"u8, 0, 1], name).Should().Be(new FileFacts(FileKind.Document, DocumentTypes.Pdf));

    [Fact]
    public void A_pdf_that_is_all_text_is_still_a_document() =>
        Inspect("%PDF-1.4\n1 0 obj << >> endobj\n%%EOF"u8.ToArray(), "plain.pdf").Kind.Should().Be(FileKind.Document);

    [Theory]
    [InlineData("lease.docx", DocumentTypes.Word)]
    [InlineData("BUDGET.XLSX", DocumentTypes.Excel)]
    [InlineData("review.pptx", DocumentTypes.PowerPoint)]
    public void A_zip_archive_named_as_an_office_document_is_one(string name, string contentType) =>
        Inspect([0x50, 0x4B, 0x03, 0x04, 0, 0, 0xFF], name).Should().Be(new FileFacts(FileKind.Document, contentType));

    [Theory]
    [InlineData("archive.zip", "application/zip")]
    [InlineData("old.doc", "application/octet-stream")]
    public void A_zip_archive_with_another_name_is_another_kind(string name, string contentType) =>
        Inspect([0x50, 0x4B, 0x03, 0x04, 0, 0, 0xFF], name).Should().Be(new FileFacts(FileKind.Other, contentType));

    [Fact]
    public void A_name_ending_in_docx_does_not_make_other_bytes_a_document() =>
        Inspect([0x00, 0x01, 0x02], "lease.docx").Should().Be(new FileFacts(FileKind.Other, "application/octet-stream"));

    [Fact]
    public void A_file_cut_inside_a_character_is_not_text() =>
        Inspect([0x61, 0xE2, 0x9C], "cut.txt").Kind.Should().Be(FileKind.Other);

    [Theory]
    [InlineData("image/png", FileKind.Image)]
    [InlineData("image/svg+xml", FileKind.Other)]
    [InlineData("text/plain", FileKind.Text)]
    [InlineData("application/yaml", FileKind.Text)]
    [InlineData("application/pdf", FileKind.Document)]
    [InlineData(DocumentTypes.Word, FileKind.Document)]
    [InlineData(DocumentTypes.Excel, FileKind.Document)]
    [InlineData(DocumentTypes.PowerPoint, FileKind.Document)]
    [InlineData("application/zip", FileKind.Other)]
    public void A_content_type_belongs_to_one_kind(string contentType, FileKind kind) =>
        FileInspector.KindOf(contentType).Should().Be(kind);

    #endregion

    #region Private Methods

    private static FileFacts Inspect(byte[] bytes, string name)
    {
        var inspector = new FileInspector();

        foreach (var chunk in bytes.Chunk(1000))
        {
            inspector.Append(chunk);
        }

        return inspector.Finish(name);
    }

    private static byte[] Png(int width, int height) =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8,
        .. BigEndian(width), .. BigEndian(height), 8, 6, 0, 0, 0,
    ];

    private static byte[] Jpeg(int width, int height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8, 0, 1, 1, 0, 0, 1, 0, 1, 0, 0,
        0xFF, 0xDB, 0x00, 0x04, 0, 0,
        0xFF, 0xC0, 0x00, 0x11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    private static byte[] Webp(string form, int width, int height)
    {
        byte[] header = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. Encoding.ASCII.GetBytes(form), 0, 0, 0, 0];
        var body = form switch
        {
            "VP8X" => (byte[])[0, 0, 0, 0, (byte)(width - 1), (byte)((width - 1) >> 8), 0, (byte)(height - 1), (byte)((height - 1) >> 8), 0],
            "VP8L" => VP8LBody(width, height),
            _ => [0, 0, 0, 0x9D, 0x01, 0x2A, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8)],
        };
        return [.. header, .. body];
    }

    private static byte[] VP8LBody(int width, int height)
    {
        var bits = (width - 1) | ((height - 1) << 14);
        return [0x2F, (byte)bits, (byte)(bits >> 8), (byte)(bits >> 16), (byte)(bits >> 24), 0, 0, 0, 0, 0];
    }

    private static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    #endregion
}
