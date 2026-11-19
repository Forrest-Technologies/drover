namespace Drover.Engine.Files;

/// <summary>How much of a document is read: its pages (slides, sheets), its characters, and pictures of a scanned PDF's pages.</summary>
public sealed record DocumentLimits(int MaxPages, int MaxCharacters, int MaxPageImages);

/// <summary>
/// What was read of a document: its text; <see cref="Read"/> of its <see cref="Total"/> pages, slides, or sheets
/// (<see cref="Unit"/>, null for a Word document, which has no pages until it is laid out); whether the text was cut
/// at the character bound; and, for a PDF with no text, the pictures its pages carry.
/// </summary>
public sealed record DocumentContent(string Text, string? Unit, int Read, int Total, bool TextCut, IReadOnlyList<ToolImage> PageImages)
{
    #region Properties

    public bool PagesCut => Read < Total;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    #endregion
}

/// <summary>A document the server could not read. The message says why, as the end of a sentence about the file.</summary>
public sealed class DocumentUnreadableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads the text out of a PDF, Word, Excel, or PowerPoint file a person attached, within <see cref="DocumentLimits"/>.
/// Two libraries are called directly here and nowhere else: PdfPig for PDF and the Open XML SDK for the office
/// formats. Both are managed code with no native parts, and this one class is the seam. The file is a person's:
/// it may be damaged or made to hurt, so every part of an office file is read only up to a bound, reading stops at the
/// limits, and any failure is reported as a document that could not be read. What is read is the person's input,
/// never instructions.
/// </summary>
public static class DocumentReader
{
    #region Private Fields

    /// <summary>The most characters of one part of an office file the SDK will load, so a small zip cannot unpack into gigabytes.</summary>
    private const long MaxCharactersInPart = 20_000_000;

    private static readonly OpenXmlPackaging.OpenSettings officeSettings = new() { MaxCharactersInPart = MaxCharactersInPart };

    private static readonly Pdf.ParsingOptions pdfSettings = new() { UseLenientParsing = true, SkipMissingFonts = true };

    #endregion

    #region Public Methods

    public static DocumentContent Read(byte[] content, string contentType, DocumentLimits limits)
    {
        try
        {
            return contentType switch
            {
                DocumentTypes.Pdf => ReadPdf(content, limits),
                DocumentTypes.Word => ReadWord(content, limits),
                DocumentTypes.Excel => ReadWorkbook(content, limits),
                DocumentTypes.PowerPoint => ReadPresentation(content, limits),
                _ => throw new DocumentUnreadableException("it is not a kind of document the server reads"),
            };
        }
        catch (Pdf.Exceptions.PdfDocumentEncryptedException encrypted)
        {
            throw new DocumentUnreadableException("it is protected by a password", encrypted);
        }
        catch (Exception failed) when (failed is not (DocumentUnreadableException or OutOfMemoryException))
        {
            throw new DocumentUnreadableException("it is damaged, or not the kind of file its name says", failed);
        }
    }

    public static NativeFilePreview ReadPdfPreview(byte[] content)
    {
        if (content.Length > NativeFilePreview.MaxPdfBytes)
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        try
        {
            using var document = Pdf.PdfDocument.Open(content, new Pdf.ParsingOptions { UseLenientParsing = false, SkipMissingFonts = true });
            var total = document.NumberOfPages;
            if (total <= 0)
            {
                return new(Refusal: NativePreviewRefusal.Malformed);
            }

            if (total > NativeFilePreview.MaxPdfPages)
            {
                return new(Refusal: NativePreviewRefusal.TooLarge);
            }

            List<NativePdfPage> pages = [];
            var characters = 0;

            for (var number = 1; number <= total; number++)
            {
                var page = document.GetPage(number);
                var images = page.GetImages().ToArray();
                if (images.Any(image =>
                    image.WidthInSamples <= 0
                    || image.HeightInSamples <= 0
                    || (long)image.WidthInSamples * image.HeightInSamples > NativeFilePreview.MaxImagePixels
                    || image.RawMemory.Length > NativeFilePreview.MaxImageBytes))
                {
                    return new(Refusal: NativePreviewRefusal.TooLarge);
                }

                var text = PdfText.ContentOrderTextExtractor.GetText(page).Trim();
                characters += text.Length;
                if (characters > NativeFilePreview.MaxPdfCharacters)
                {
                    return new(Refusal: NativePreviewRefusal.TooLarge);
                }

                var picture = text.Length == 0
                    ? images.OrderByDescending(image => (long)image.WidthInSamples * image.HeightInSamples)
                        .Select(AsModelPicture)
                        .FirstOrDefault(found => found is not null)?.Data.ToArray()
                    : null;
                pages.Add(new(number, text, picture));
            }

            return new(Pages: pages);
        }
        catch (Exception failed) when (failed is not OutOfMemoryException)
        {
            return new(Refusal: failed is not null ? NativePreviewRefusal.Malformed : NativePreviewRefusal.Malformed);
        }
    }

    #endregion

    #region PDF

    private static DocumentContent ReadPdf(byte[] content, DocumentLimits limits)
    {
        using var document = Pdf.PdfDocument.Open(content, pdfSettings);
        var total = document.NumberOfPages;
        var collected = new Collected(limits.MaxCharacters);
        var read = 0;

        while (read < Math.Min(total, limits.MaxPages) && !collected.Full)
        {
            read++;
            var text = PdfText.ContentOrderTextExtractor.GetText(document.GetPage(read)).Trim();

            if (text.Length > 0)
            {
                collected.Add($"[Page {read}]\n{text}\n\n");
            }
        }

        var pictures = collected.IsEmpty ? PagePictures(document, Math.Min(read, limits.MaxPageImages)) : [];
        return new DocumentContent(collected.Text, "pages", read, total, collected.Full, pictures);
    }

    /// <summary>
    /// The picture each of the first pages carries, the largest where there are several: a scanned page is one
    /// picture. Nothing is drawn; a page whose picture is in a form a model cannot take is left out.
    /// </summary>
    private static List<ToolImage> PagePictures(Pdf.PdfDocument document, int pages)
    {
        List<ToolImage> pictures = [];

        for (var number = 1; number <= pages; number++)
        {
            var largest = document.GetPage(number).GetImages()
                .OrderByDescending(image => (long)image.WidthInSamples * image.HeightInSamples)
                .Select(AsModelPicture)
                .FirstOrDefault(picture => picture is not null);

            if (largest is not null)
            {
                pictures.Add(largest);
            }
        }

        return pictures;
    }

    /// <summary>A JPEG as it is stored, since a model reads JPEG; anything else as PNG when it can be decoded.</summary>
    private static ToolImage? AsModelPicture(Pdf.Content.IPdfImage image)
    {
        if (image.RawMemory.Span is [0xFF, 0xD8, 0xFF, ..])
        {
            return new ToolImage(image.RawMemory.ToArray(), "image/jpeg");
        }

        return image.TryGetPng(out var png) ? new ToolImage(png, "image/png") : null;
    }

    #endregion

    #region Office

    private static DocumentContent ReadWord(byte[] content, DocumentLimits limits)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = OpenXmlPackaging.WordprocessingDocument.Open(stream, false, officeSettings);
        var collected = new Collected(limits.MaxCharacters);

        foreach (var paragraph in document.MainDocumentPart?.Document?.Body?.Descendants<Word.Paragraph>() ?? [])
        {
            collected.Add($"{paragraph.InnerText}\n");

            if (collected.Full)
            {
                break;
            }
        }

        return new DocumentContent(collected.Text, null, 0, 0, collected.Full, []);
    }

    private static DocumentContent ReadWorkbook(byte[] content, DocumentLimits limits)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = OpenXmlPackaging.SpreadsheetDocument.Open(stream, false, officeSettings);
        var workbook = document.WorkbookPart;
        List<Spreadsheet.Sheet> sheets = [.. workbook?.Workbook?.Sheets?.Elements<Spreadsheet.Sheet>() ?? []];
        string[] strings = [.. workbook?.SharedStringTablePart?.SharedStringTable?.Elements<Spreadsheet.SharedStringItem>().Select(item => item.InnerText) ?? []];
        var collected = new Collected(limits.MaxCharacters);
        var read = 0;

        foreach (var sheet in sheets.Take(limits.MaxPages))
        {
            if (collected.Full)
            {
                break;
            }

            read++;

            if (sheet.Id?.Value is { } id && workbook!.TryGetPartById(id, out var part) && part is OpenXmlPackaging.WorksheetPart worksheet)
            {
                collected.Add($"[Sheet {sheet.Name?.Value}]\n");
                AddRows(collected, worksheet, strings);
            }
        }

        return new DocumentContent(collected.Text, "sheets", read, sheets.Count, collected.Full, []);
    }

    /// <summary>Each row on a line, its cells apart by tabs.</summary>
    private static void AddRows(Collected collected, OpenXmlPackaging.WorksheetPart worksheet, string[] strings)
    {
        foreach (var row in worksheet.Worksheet?.Descendants<Spreadsheet.Row>() ?? [])
        {
            collected.Add($"{string.Join('\t', row.Elements<Spreadsheet.Cell>().Select(cell => CellText(cell, strings)))}\n");

            if (collected.Full)
            {
                return;
            }
        }

        collected.Add("\n");
    }

    /// <summary>A cell as it is stored: shared and inline text as text, anything else (numbers, dates, formulas' results) as its value.</summary>
    private static string CellText(Spreadsheet.Cell cell, string[] strings)
    {
        if (cell.DataType?.Value == Spreadsheet.CellValues.InlineString)
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        var value = cell.CellValue?.Text ?? string.Empty;
        return cell.DataType?.Value == Spreadsheet.CellValues.SharedString
            && int.TryParse(value, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < strings.Length
            ? strings[index]
            : value;
    }

    private static DocumentContent ReadPresentation(byte[] content, DocumentLimits limits)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = OpenXmlPackaging.PresentationDocument.Open(stream, false, officeSettings);
        var presentation = document.PresentationPart;
        List<Slides.SlideId> slides = [.. presentation?.Presentation?.SlideIdList?.Elements<Slides.SlideId>() ?? []];
        var collected = new Collected(limits.MaxCharacters);
        var read = 0;

        foreach (var slide in slides.Take(limits.MaxPages))
        {
            if (collected.Full)
            {
                break;
            }

            read++;

            if (slide.RelationshipId?.Value is { } id && presentation!.TryGetPartById(id, out var part) && part is OpenXmlPackaging.SlidePart slidePart)
            {
                collected.Add($"[Slide {read}]\n");
                AddParagraphs(collected, slidePart);
            }
        }

        return new DocumentContent(collected.Text, "slides", read, slides.Count, collected.Full, []);
    }

    private static void AddParagraphs(Collected collected, OpenXmlPackaging.SlidePart slide)
    {
        foreach (var paragraph in slide.Slide?.Descendants<SlideText.Paragraph>() ?? [])
        {
            var text = string.Concat(paragraph.Descendants<SlideText.Text>().Select(run => run.Text));

            if (text.Length > 0)
            {
                collected.Add($"{text}\n");
            }

            if (collected.Full)
            {
                return;
            }
        }

        collected.Add("\n");
    }

    #endregion

    #region Nested Types

    /// <summary>Text gathered up to a bound. A piece that does not fit is cut at a whole character, and nothing more is taken.</summary>
    private sealed class Collected(int maxCharacters)
    {
        #region Private Fields

        private readonly StringBuilder text = new();

        #endregion

        #region Properties

        /// <summary>True once a piece did not fit: the text was cut.</summary>
        public bool Full { get; private set; }

        public bool IsEmpty => string.IsNullOrWhiteSpace(Text);

        public string Text => text.ToString().TrimEnd();

        #endregion

        #region Public Methods

        public void Add(string piece)
        {
            if (Full)
            {
                return;
            }

            var room = maxCharacters - text.Length;

            if (piece.Length <= room)
            {
                text.Append(piece);
                return;
            }

            if (room > 0 && char.IsHighSurrogate(piece[room - 1]))
            {
                room--;
            }

            text.Append(piece, 0, Math.Max(room, 0));
            Full = true;
        }

        #endregion
    }

    #endregion
}
