namespace Drover.Engine.Tests.Files;

/// <summary>Bounded predictor decoding used by PDF native previews.</summary>
public sealed class PdfPreviewFiltersTests
{
    #region Public Methods

    [Fact]
    public void Decodes_bounded_png_predictor_rows()
    {
        var parameters = Dictionary(
            (UglyToad.PdfPig.Tokens.NameToken.Predictor, new UglyToad.PdfPig.Tokens.NumericToken(12)),
            (UglyToad.PdfPig.Tokens.NameToken.Colors, new UglyToad.PdfPig.Tokens.NumericToken(1)),
            (UglyToad.PdfPig.Tokens.NameToken.BitsPerComponent, new UglyToad.PdfPig.Tokens.NumericToken(8)),
            (UglyToad.PdfPig.Tokens.NameToken.Columns, new UglyToad.PdfPig.Tokens.NumericToken(3)));
        var dictionary = StreamDictionary(parameters);
        var filters = new PdfPreviewFilters(CancellationToken.None, maxStreamBytes: 64);
        var encoded = Compress([2, 1, 2, 3, 2, 3, 4, 5]);

        var decoded = filters.GetFilters(dictionary).Single().Decode(encoded, dictionary, filters, 0);

        decoded.ToArray().Should().Equal(1, 2, 3, 4, 6, 8);
        filters.Refusal.Should().BeNull();
    }

    [Fact]
    public void Decodes_bounded_tiff_predictor_rows()
    {
        var parameters = Dictionary(
            (UglyToad.PdfPig.Tokens.NameToken.Predictor, new UglyToad.PdfPig.Tokens.NumericToken(2)),
            (UglyToad.PdfPig.Tokens.NameToken.Colors, new UglyToad.PdfPig.Tokens.NumericToken(1)),
            (UglyToad.PdfPig.Tokens.NameToken.BitsPerComponent, new UglyToad.PdfPig.Tokens.NumericToken(8)),
            (UglyToad.PdfPig.Tokens.NameToken.Columns, new UglyToad.PdfPig.Tokens.NumericToken(4)));
        var dictionary = StreamDictionary(parameters);
        var filters = new PdfPreviewFilters(CancellationToken.None, maxStreamBytes: 64);
        var encoded = Compress([3, 2, 3, 4]);

        var decoded = filters.GetFilters(dictionary).Single().Decode(encoded, dictionary, filters, 0);

        decoded.ToArray().Should().Equal(3, 5, 8, 12);
        filters.Refusal.Should().BeNull();
    }

    [Fact]
    public void Refuses_indirect_predictor_parameters()
    {
        var reference = new UglyToad.PdfPig.Tokens.IndirectReferenceToken(
            new UglyToad.PdfPig.Core.IndirectReference(2, 0));
        var dictionary = StreamDictionary(reference);
        var filters = new PdfPreviewFilters(CancellationToken.None, maxStreamBytes: 64);
        var encoded = Compress([0]);

        var decode = () => filters.GetFilters(dictionary).Single().Decode(encoded, dictionary, filters, 0);

        decode.Should().Throw<InvalidDataException>();
        filters.Refusal.Should().Be(NativePreviewRefusal.TooComplex);
    }

    [Fact]
    public void Refuses_predictor_input_over_stream_budget()
    {
        var parameters = Dictionary(
            (UglyToad.PdfPig.Tokens.NameToken.Predictor, new UglyToad.PdfPig.Tokens.NumericToken(12)),
            (UglyToad.PdfPig.Tokens.NameToken.Columns, new UglyToad.PdfPig.Tokens.NumericToken(3)));
        var dictionary = StreamDictionary(parameters);
        var filters = new PdfPreviewFilters(CancellationToken.None, maxStreamBytes: 7);
        var encoded = Compress([2, 1, 2, 3, 2, 3, 4, 5]);

        var decode = () => filters.GetFilters(dictionary).Single().Decode(encoded, dictionary, filters, 0);

        decode.Should().Throw<InvalidDataException>();
        filters.Refusal.Should().Be(NativePreviewRefusal.TooLarge);
    }

    [Fact]
    public void Reads_pdf_with_png_predicted_cross_reference_stream()
    {
        var result = DocumentReader.ReadPdfPreview(
            PredictedCrossReferencePdf(),
            maxStreamBytes: 64,
            cancellationToken: TestContext.Current.CancellationToken);

        result.Refusal.Should().BeNull();
        result.Pages.Should().ContainSingle();
    }

    #endregion

    #region Private Methods

    private static byte[] Compress(byte[] content)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(content);
        }

        return compressed.ToArray();
    }

    private static UglyToad.PdfPig.Tokens.DictionaryToken Dictionary(
        params (UglyToad.PdfPig.Tokens.NameToken Key, UglyToad.PdfPig.Tokens.IToken Value)[] entries) =>
        new(entries.ToDictionary(entry => entry.Key, entry => entry.Value));

    private static byte[] EncodePngUp(byte[] content, int columns)
    {
        var rows = content.Length / columns;
        var encoded = new byte[rows * (columns + 1)];
        for (var row = 0; row < rows; row++)
        {
            var encodedOffset = row * (columns + 1);
            var contentOffset = row * columns;
            encoded[encodedOffset] = 2;
            for (var column = 0; column < columns; column++)
            {
                var above = row == 0 ? 0 : content[contentOffset - columns + column];
                encoded[encodedOffset + column + 1] = unchecked((byte)(content[contentOffset + column] - above));
            }
        }

        return encoded;
    }

    private static byte[] PredictedCrossReferencePdf()
    {
        using var pdf = new MemoryStream();
        var offsets = new long[5];
        Write("%PDF-1.5\n");
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, "<< /Type /Pages /Count 1 /Kids [3 0 R] >>");
        WriteObject(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] >>");

        offsets[4] = pdf.Position;
        var entries = new byte[5 * 7];
        WriteEntry(entries.AsSpan(0, 7), 0, 0, ushort.MaxValue);
        for (var objectNumber = 1; objectNumber < offsets.Length; objectNumber++)
        {
            WriteEntry(entries.AsSpan(objectNumber * 7, 7), 1, offsets[objectNumber], 0);
        }

        var compressed = Compress(EncodePngUp(entries, columns: 7));
        Write($"""
            4 0 obj
            << /Type /XRef /Size 5 /W [1 4 2] /Root 1 0 R /Length {compressed.Length}
               /Filter /FlateDecode /DecodeParms << /Predictor 12 /Columns 7 >> >>
            stream
            """);
        Write("\n");
        pdf.Write(compressed);
        Write($"\nendstream\nendobj\nstartxref\n{offsets[4]}\n%%EOF\n");
        return pdf.ToArray();

        void Write(string text) => pdf.Write(Encoding.ASCII.GetBytes(text));

        void WriteObject(int objectNumber, string value)
        {
            offsets[objectNumber] = pdf.Position;
            Write($"{objectNumber} 0 obj\n{value}\nendobj\n");
        }
    }

    private static UglyToad.PdfPig.Tokens.DictionaryToken StreamDictionary(
        UglyToad.PdfPig.Tokens.IToken parameters) =>
        Dictionary(
            (UglyToad.PdfPig.Tokens.NameToken.Filter, UglyToad.PdfPig.Tokens.NameToken.FlateDecode),
            (UglyToad.PdfPig.Tokens.NameToken.DecodeParms, parameters));

    private static void WriteEntry(Span<byte> entry, byte type, long offset, ushort generation)
    {
        entry[0] = type;
        BinaryPrimitives.WriteUInt32BigEndian(entry[1..], checked((uint)offset));
        BinaryPrimitives.WriteUInt16BigEndian(entry[5..], generation);
    }

    #endregion
}
