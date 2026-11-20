namespace Drover.Engine.Files;

/// <summary>
/// How much one document may unpack: the most bytes one Flate stream and all of them together may produce, the most
/// streams the parser may decode, and whether only the filters a preview needs are available.
/// </summary>
internal sealed record PdfDecodeBudget(int StreamBytes, int TotalBytes, int Streams, bool PreviewFiltersOnly)
{
    #region Constants

    /// <summary>A native preview: one Flate or DCT filter per stream, small bounds.</summary>
    public static readonly PdfDecodeBudget Preview = new(
        NativeFilePreview.MaxPdfDecodedStreamBytes,
        NativeFilePreview.MaxPdfTotalDecodedBytes,
        NativeFilePreview.MaxPdfStreams,
        PreviewFiltersOnly: true);

    /// <summary>
    /// Reading a document's text for search and attachments: a large scan may unpack to tens of megabytes, so the
    /// bounds are wider, but a small file can still never unpack to gigabytes.
    /// </summary>
    public static readonly PdfDecodeBudget Reading = new(
        64 * 1024 * 1024,
        128 * 1024 * 1024,
        int.MaxValue,
        PreviewFiltersOnly: false);

    #endregion
}

/// <summary>
/// The only filters the parser may use. Every Flate decode, including its row predictor, shares one document
/// budget, and a filter whose output cannot be bounded is not offered.
/// </summary>
internal sealed class PdfPreviewFilters(PdfDecodeBudget budget, CancellationToken cancellationToken)
    : PdfFilters.IFilterProvider
{
    #region Private Fields

    /// <summary>
    /// The filters, beside Flate and DCT, a document read allows. Each shrinks or keeps its input within a small
    /// ratio. LZW is left out: one code can unpack to thousands of bytes, so its output cannot be bounded.
    /// </summary>
    private static readonly string[] readingFilterNames =
    [
        "ASCII85Decode", "A85", "ASCIIHexDecode", "AHx", "RunLengthDecode", "RL", "CCITTFaxDecode", "CCF",
        "JBIG2Decode", "JPXDecode",
    ];

    private readonly PreviewFilterNames names = new(new BoundedFlate(budget, cancellationToken), budget);

    #endregion

    #region Properties

    public NativePreviewRefusal? Refusal => names.Flate.Refusal;

    #endregion

    #region Public Methods

    public void ReserveRawPageContents(Pdf.Structure structure, PdfTokens.DictionaryToken page)
    {
        if (!page.TryGet(PdfTokens.NameToken.Contents, out var contents))
        {
            return;
        }

        var scanner = structure.TokenScanner;
        if (PdfObjects.DirectObjectFinder.TryGet<PdfTokens.ArrayToken>(contents, scanner, out var array))
        {
            if (array.Data.Count > NativeFilePreview.MaxPdfStreams)
            {
                names.Flate.Refuse(NativePreviewRefusal.TooComplex);
            }

            foreach (var item in array.Data)
            {
                ReserveRawStream(structure, item);
            }
        }
        else
        {
            ReserveRawStream(structure, contents);
        }
    }

    #endregion

    #region Interface Implementations

    public IReadOnlyList<PdfFilters.IFilter> GetFilters(PdfTokens.DictionaryToken dictionary) =>
        CheckFilters(names.GetFilters(dictionary));

    public IReadOnlyList<PdfFilters.IFilter> GetNamedFilters(IReadOnlyList<PdfTokens.NameToken> filterNames) =>
        CheckFilters(names.GetNamedFilters(filterNames));

    public IReadOnlyList<PdfFilters.IFilter> GetAllFilters() => names.GetAllFilters();

    #endregion

    #region Private Methods

    private void ReserveRawStream(Pdf.Structure structure, PdfTokens.IToken content)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scanner = structure.TokenScanner;
        var stream = PdfObjects.DirectObjectFinder.Get<PdfTokens.StreamToken>(content, scanner);
        if (stream is not null && structure.FilterProvider.GetFilters(stream.StreamDictionary, scanner).Count == 0)
        {
            names.Flate.ReserveBytes(stream.Data.Length);
        }
    }

    private IReadOnlyList<PdfFilters.IFilter> CheckFilters(IReadOnlyList<PdfFilters.IFilter> filters)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (filters.Count > 1 && budget.PreviewFiltersOnly)
        {
            names.Flate.Refuse(NativePreviewRefusal.TooComplex);
        }

        return filters;
    }

    #endregion

    #region Nested Types

    private sealed class PreviewFilterNames(BoundedFlate flate, PdfDecodeBudget budget)
        : PdfFilters.BaseFilterProvider(BuildFilters(flate, budget))
    {
        #region Properties

        public BoundedFlate Flate => flate;

        #endregion

        #region Private Methods

        private static Dictionary<string, PdfFilters.IFilter> BuildFilters(BoundedFlate flate, PdfDecodeBudget budget)
        {
            var filters = new Dictionary<string, PdfFilters.IFilter>
            {
                [PdfTokens.NameToken.FlateDecode.Data] = flate,
                [PdfTokens.NameToken.FlateDecodeAbbreviation.Data] = flate,
                [PdfTokens.NameToken.DctDecode.Data] = new PdfFilters.DctDecodeFilter(),
                [PdfTokens.NameToken.DctDecodeAbbreviation.Data] = new PdfFilters.DctDecodeFilter(),
            };

            if (!budget.PreviewFiltersOnly)
            {
                foreach (var name in readingFilterNames)
                {
                    filters[name] = PdfFilters.DefaultFilterProvider.Instance
                        .GetNamedFilters([PdfTokens.NameToken.Create(name)])[0];
                }
            }

            return filters;
        }

        #endregion
    }

    private sealed class BoundedFlate(PdfDecodeBudget budget, CancellationToken cancellationToken) : PdfFilters.IFilter
    {
        #region Private Fields

        private int totalDecoded;
        private int streamsDecoded;

        #endregion

        #region Properties

        public bool IsSupported => true;

        public NativePreviewRefusal? Refusal { get; private set; }

        #endregion

        #region Public Methods

        public Memory<byte> Decode(
            Memory<byte> input,
            PdfTokens.DictionaryToken streamDictionary,
            PdfFilters.IFilterProvider filterProvider,
            int filterIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++streamsDecoded > budget.Streams)
            {
                Refuse(NativePreviewRefusal.TooComplex);
            }

            var parameters = PdfFilters.DecodeParameterResolver.GetFilterParameters(streamDictionary, filterIndex);
            var shape = ReadPredictorShape(parameters);
            var limit = Math.Min(budget.StreamBytes, budget.TotalBytes - totalDecoded);
            using var source = new MemoryStream(input.ToArray(), writable: false);
            using var decompressor = new ZLibStream(source, CompressionMode.Decompress);
            using var result = new MemoryStream();
            Span<byte> buffer = stackalloc byte[8192];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = decompressor.Read(buffer[..Math.Min(buffer.Length, limit - (int)result.Length + 1)]);
                if (read == 0)
                {
                    return Unpredict(result, shape);
                }

                var length = (int)result.Length + read;
                if (length > limit)
                {
                    Refuse(NativePreviewRefusal.TooLarge);
                }

                if (result.Capacity < length)
                {
                    result.Capacity = Math.Min(limit, Math.Max(length, result.Capacity * 2));
                }

                totalDecoded += read;
                result.Write(buffer[..read]);
            }
        }

        public void ReserveBytes(int length)
        {
            if (length > budget.TotalBytes - totalDecoded)
            {
                Refuse(NativePreviewRefusal.TooLarge);
            }

            totalDecoded += length;
        }

        public void Refuse(NativePreviewRefusal refusal)
        {
            Refusal ??= refusal;
            throw new InvalidDataException("PDF preview exceeded a decode limit.");
        }

        #endregion

        #region Predictors

        /// <summary>The predictor a stream names, or null when it has none. A value that is not a plain number is refused.</summary>
        private PdfPredictorShape? ReadPredictorShape(PdfTokens.DictionaryToken parameters)
        {
            if (!parameters.TryGet(PdfTokens.NameToken.Predictor, out var token))
            {
                return null;
            }

            var predictor = NumberOrRefuse(token);
            if (predictor == 1)
            {
                return null;
            }

            return new(
                predictor,
                ReadParameter(parameters, PdfTokens.NameToken.Colors, 1),
                ReadParameter(parameters, PdfTokens.NameToken.BitsPerComponent, 8),
                ReadParameter(parameters, PdfTokens.NameToken.Columns, 1));
        }

        private int ReadParameter(PdfTokens.DictionaryToken parameters, PdfTokens.NameToken name, int fallback) =>
            parameters.TryGet(name, out var token) ? NumberOrRefuse(token) : fallback;

        private int NumberOrRefuse(PdfTokens.IToken token)
        {
            if (token is PdfTokens.NumericToken { Double: >= 0 and <= int.MaxValue } number && number.Double == number.Int)
            {
                return number.Int;
            }

            Refuse(NativePreviewRefusal.TooComplex);
            return 0;
        }

        private Memory<byte> Unpredict(MemoryStream decoded, PdfPredictorShape? shape)
        {
            var memory = decoded.GetBuffer().AsMemory(0, (int)decoded.Length);
            if (shape is not { } predicted)
            {
                return memory;
            }

            if (PdfPredictor.Decode(memory.Span, predicted, out var length) is { } refusal)
            {
                Refuse(refusal);
            }

            return memory[..length];
        }

        #endregion
    }

    #endregion
}
