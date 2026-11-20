namespace Drover.Engine.Tests.Files;

/// <summary>Small PDF and office documents, made in the test with the same libraries the server reads them with.</summary>
public static class TestDocuments
{
    #region Public Methods

    /// <summary>A PDF with one line of text on each page.</summary>
    public static byte[] Pdf(params string[] pages)
    {
        using var builder = new PdfWriter.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(PdfFonts.Standard14Font.Helvetica);

        foreach (var text in pages)
        {
            builder.AddPage(PdfContent.PageSize.A4).AddText(text, 12, new PdfCore.PdfPoint(50, 700), font);
        }

        return builder.Build();
    }

    /// <summary>A PDF whose pages are each one picture and no text, as a scanner makes them.</summary>
    public static byte[] ScannedPdf(params byte[][] jpegs)
    {
        using var builder = new PdfWriter.PdfDocumentBuilder();

        foreach (var jpeg in jpegs)
        {
            builder.AddPage(PdfContent.PageSize.A4).AddJpeg(jpeg, new PdfCore.PdfRectangle(0, 0, 595, 842));
        }

        return builder.Build();
    }

    /// <summary>
    /// The smallest JPEG a reader takes the size of: the start, one frame header, the end. It has no picture data, so
    /// the byte after the size tells two apart.
    /// </summary>
    public static byte[] Jpeg(int width, int height, byte mark) =>
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x0B, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x01, 0x01, 0x11, mark,
        0xFF, 0xD9,
    ];

    public static byte[] Docx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();

        using (var document = OpenXmlPackaging.WordprocessingDocument.Create(stream, OpenXml.WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Word.Document(
                new Word.Body(paragraphs.Select(text => new Word.Paragraph(new Word.Run(new Word.Text(text))))));
        }

        return stream.ToArray();
    }

    /// <summary>A workbook with one sheet per entry, its first cell text shared between cells and the rest inline.</summary>
    public static byte[] Xlsx(params (string Name, string[][] Rows)[] sheets)
    {
        using var stream = new MemoryStream();

        using (var document = OpenXmlPackaging.SpreadsheetDocument.Create(stream, OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Spreadsheet.Workbook(new Spreadsheet.Sheets());
            var shared = workbook.AddNewPart<OpenXmlPackaging.SharedStringTablePart>();
            shared.SharedStringTable = new Spreadsheet.SharedStringTable(new Spreadsheet.SharedStringItem(new Spreadsheet.Text("Shared heading")));
            uint number = 1;

            foreach (var (name, rows) in sheets)
            {
                var part = workbook.AddNewPart<OpenXmlPackaging.WorksheetPart>();
                var heading = new Spreadsheet.Row(new Spreadsheet.Cell { DataType = Spreadsheet.CellValues.SharedString, CellValue = new Spreadsheet.CellValue("0") });
                part.Worksheet = new Spreadsheet.Worksheet(new Spreadsheet.SheetData([heading, .. rows.Select(row => new Spreadsheet.Row(row.Select(Cell)))]));
                workbook.Workbook.Sheets!.Append(new Spreadsheet.Sheet { Id = workbook.GetIdOfPart(part), SheetId = number++, Name = name });
            }
        }

        return stream.ToArray();
    }

    /// <summary>A presentation with one line of text on each slide. It has no masters: the reader needs only the slides.</summary>
    public static byte[] Pptx(params string[] slides)
    {
        using var stream = new MemoryStream();

        using (var document = OpenXmlPackaging.PresentationDocument.Create(stream, OpenXml.PresentationDocumentType.Presentation))
        {
            var presentation = document.AddPresentationPart();
            presentation.Presentation = new Slides.Presentation(new Slides.SlideIdList());
            uint id = 256;

            foreach (var text in slides)
            {
                var slide = presentation.AddNewPart<OpenXmlPackaging.SlidePart>();
                slide.Slide = new Slides.Slide(new Slides.CommonSlideData(new Slides.ShapeTree(new Slides.Shape(
                    new Slides.TextBody(new SlideText.BodyProperties(), new SlideText.Paragraph(new SlideText.Run(new SlideText.Text(text))))))));
                presentation.Presentation.SlideIdList!.Append(new Slides.SlideId { Id = id++, RelationshipId = presentation.GetIdOfPart(slide) });
            }
        }

        return stream.ToArray();
    }

    public static byte[] PdfCommands(int length)
    {
        var bytes = new byte[length];
        Array.Fill(bytes, (byte)' ');
        "BT /F1 12 Tf (Safe content) Tj ET\n"u8.CopyTo(bytes);
        return bytes;
    }

    public static byte[] CompressPdfCommands(byte[] content)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(content);
        }

        return compressed.ToArray();
    }

    public static byte[] ContentStreamPdf(
        string filter, byte[][] streams, string streamNewline = "\n", string afterDictionary = "")
    {
        using var pdf = new MemoryStream();
        List<long> offsets = [];
        var references = string.Join(' ', Enumerable.Range(6, streams.Length).Select(id => $"{id} 0 R"));
        Write("%PDF-1.7\n");
        WriteObject("<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject("<< /Type /Pages /Count 1 /Kids [3 0 R] >>");
        WriteObject(
            $"""
            << /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792]
               /Contents [{references}] /Resources << /Font << /F1 4 0 R >> >> >>
            """);
        WriteObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        WriteObject("/FlateDecode");

        foreach (var stream in streams)
        {
            StartObject();
            Write($"<< {filter} /Length {stream.Length} >>{afterDictionary}\nstream{streamNewline}");
            pdf.Write(stream);
            Write("\nendstream\nendobj\n");
        }

        var xref = pdf.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();

        void Write(string text) => pdf.Write(Encoding.ASCII.GetBytes(text));

        void StartObject()
        {
            offsets.Add(pdf.Position);
            Write($"{offsets.Count} 0 obj\n");
        }

        void WriteObject(string value)
        {
            StartObject();
            Write($"{value}\nendobj\n");
        }
    }

    /// <summary>
    /// A zlib stream of a comment holding the text <c>endstream</c> and then <paramref name="content"/>. The comment
    /// sits in a stored block, so the text appears as it is in the compressed bytes.
    /// </summary>
    public static byte[] FlateWithLiteralStreamEnd(byte[] content)
    {
        var literal = "% endstream\n"u8.ToArray();
        var rest = content;
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(rest);
        }

        using var result = new MemoryStream();
        result.Write([0x78, 0x01, 0x00, (byte)literal.Length, 0x00, (byte)~literal.Length, 0xFF]);
        result.Write(literal);
        result.Write(packed.ToArray());
        var checksum = Adler32([.. literal, .. rest]);
        result.Write([(byte)(checksum >> 24), (byte)(checksum >> 16), (byte)(checksum >> 8), (byte)checksum]);
        return result.ToArray();
    }

    /// <summary>Run length data: each of <paramref name="runs"/> repeats one byte 128 times, so it unpacks 64 fold.</summary>
    public static byte[] RunLengthRuns(int runs)
    {
        var data = new byte[(runs * 2) + 1];
        for (var run = 0; run < runs; run++)
        {
            data[run * 2] = 129;
            data[(run * 2) + 1] = (byte)' ';
        }

        data[^1] = 128;
        return data;
    }

    /// <summary>Run length data that copies each of <paramref name="pairs"/> bytes as it is, so it unpacks to half its size.</summary>
    public static byte[] RunLengthLiterals(int pairs)
    {
        var data = new byte[(pairs * 2) + 1];
        for (var pair = 0; pair < pairs; pair++)
        {
            data[pair * 2] = 0;
            data[(pair * 2) + 1] = (byte)' ';
        }

        data[^1] = 128;
        return data;
    }

    /// <summary>The bytes as ASCII base 85 text, with its end marker.</summary>
    public static byte[] Ascii85(byte[] data)
    {
        var text = new StringBuilder();
        for (var index = 0; index < data.Length; index += 4)
        {
            var count = Math.Min(4, data.Length - index);
            uint value = 0;
            for (var offset = 0; offset < 4; offset++)
            {
                value = (value << 8) | (offset < count ? data[index + offset] : (byte)0);
            }

            var digits = new char[5];
            for (var digit = 4; digit >= 0; digit--)
            {
                digits[digit] = (char)('!' + (value % 85));
                value /= 85;
            }

            text.Append(digits, 0, count + 1);
        }

        return Encoding.ASCII.GetBytes($"{text}~>");
    }

    /// <summary>The bytes as LZW codes of nine bits. It is for inputs short enough that the table never outgrows them.</summary>
    public static byte[] Lzw(byte[] data)
    {
        var table = new Dictionary<(int Prefix, byte Next), int>();
        List<int> codes = [256];
        var next = 258;
        var current = -1;
        foreach (var value in data)
        {
            if (current < 0)
            {
                current = value;
            }
            else if (table.TryGetValue((current, value), out var known))
            {
                current = known;
            }
            else
            {
                codes.Add(current);
                table[(current, value)] = next++;
                current = value;
            }
        }

        codes.AddRange([current, 257]);
        List<byte> packed = [];
        var buffer = 0;
        var bits = 0;
        foreach (var code in codes)
        {
            buffer = (buffer << 9) | code;
            bits += 9;
            while (bits >= 8)
            {
                packed.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        if (bits > 0)
        {
            packed.Add((byte)(buffer << (8 - bits)));
        }

        return [.. packed];
    }

    /// <summary>
    /// The rows of <paramref name="raw"/> as a PNG predictor stores them, each tagged with <paramref name="tag"/>
    /// (0 none, 1 left, 2 up, 3 average, 4 Paeth). The left neighbour is <paramref name="bytesPerPixel"/> bytes back:
    /// a pixel's bits rounded up to whole bytes.
    /// </summary>
    public static byte[] PngPredicted(byte[] raw, int rowBytes, byte tag, int bytesPerPixel = 1)
    {
        List<byte> predicted = [];
        for (var row = 0; row < raw.Length / rowBytes; row++)
        {
            predicted.Add(tag);
            for (var column = 0; column < rowBytes; column++)
            {
                var left = column >= bytesPerPixel ? raw[(row * rowBytes) + column - bytesPerPixel] : 0;
                var up = row > 0 ? raw[((row - 1) * rowBytes) + column] : 0;
                var upLeft = row > 0 && column >= bytesPerPixel ? raw[((row - 1) * rowBytes) + column - bytesPerPixel] : 0;
                var estimate = tag switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };

                predicted.Add((byte)(raw[(row * rowBytes) + column] - estimate));
            }
        }

        return [.. predicted];
    }

    /// <summary>The rows of <paramref name="raw"/> as the TIFF predictor stores them: each byte less the one before it.</summary>
    public static byte[] TiffPredicted(byte[] raw, int rowBytes)
    {
        var predicted = raw.ToArray();
        for (var index = 0; index < raw.Length; index++)
        {
            if (index % rowBytes > 0)
            {
                predicted[index] = (byte)(raw[index] - raw[index - 1]);
            }
        }

        return predicted;
    }

    /// <summary>
    /// A PDF that keeps its object offsets in a compressed cross reference stream with the PNG Up predictor, as most
    /// writers do, and has one page of text.
    /// </summary>
    public static byte[] XrefStreamPdf()
    {
        using var pdf = new MemoryStream();
        List<int> offsets = [];
        Write("%PDF-1.7\n");
        WriteObject("<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject("<< /Type /Pages /Count 1 /Kids [3 0 R] >>");
        WriteObject(
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 4 0 R >> >> >>");
        WriteObject("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        var commands = PdfCommands(64);
        offsets.Add((int)pdf.Position);
        Write($"{offsets.Count} 0 obj\n<< /Length {commands.Length} >>\nstream\n");
        pdf.Write(commands);
        Write("\nendstream\nendobj\n");
        var xref = (int)pdf.Position;
        offsets.Add(xref);
        byte[] rows = [0, 0, 0, 255, .. offsets.SelectMany(offset => new byte[] { 1, (byte)(offset >> 8), (byte)offset, 0 })];
        var stream = CompressPdfCommands(PngPredicted(rows, 4, 2));
        Write(
            $"""
            {offsets.Count} 0 obj
            << /Type /XRef /Size {offsets.Count + 1} /W [1 2 1] /Root 1 0 R /Filter /FlateDecode
               /DecodeParms << /Predictor 12 /Columns 4 >> /Length {stream.Length} >>
            stream

            """);
        pdf.Write(stream);
        Write($"\nendstream\nendobj\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();

        void Write(string text) => pdf.Write(Encoding.ASCII.GetBytes(text));

        void WriteObject(string value)
        {
            offsets.Add((int)pdf.Position);
            Write($"{offsets.Count} 0 obj\n{value}\nendobj\n");
        }
    }

    #endregion

    #region Private Methods

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }

    private static uint Adler32(byte[] data)
    {
        uint low = 1;
        uint high = 0;
        foreach (var value in data)
        {
            low = (low + value) % 65521;
            high = (high + low) % 65521;
        }

        return (high << 16) | low;
    }

    private static Spreadsheet.Cell Cell(string value) =>
        double.TryParse(value, CultureInfo.InvariantCulture, out _)
            ? new Spreadsheet.Cell { CellValue = new Spreadsheet.CellValue(value) }
            : new Spreadsheet.Cell
            {
                DataType = Spreadsheet.CellValues.InlineString,
                InlineString = new Spreadsheet.InlineString(new Spreadsheet.Text(value)),
            };

    #endregion
}
