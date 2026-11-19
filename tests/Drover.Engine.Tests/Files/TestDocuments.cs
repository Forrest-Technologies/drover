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

    #endregion

    #region Private Methods

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
