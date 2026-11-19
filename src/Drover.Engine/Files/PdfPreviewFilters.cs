namespace Drover.Engine.Files;

/// <summary>Only preview-safe filters are available to the parser. Every Flate decode shares one document budget.</summary>
internal sealed class PdfPreviewFilters(CancellationToken cancellationToken) : PdfFilters.IFilterProvider
{
    #region Private Fields

    private readonly PreviewFilterNames names = new(new BoundedFlate(cancellationToken));

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
        if (filters.Count > 1)
        {
            names.Flate.Refuse(NativePreviewRefusal.TooComplex);
        }

        return filters;
    }

    #endregion

    #region Nested Types

    private sealed class PreviewFilterNames(BoundedFlate flate) : PdfFilters.BaseFilterProvider(
        new Dictionary<string, PdfFilters.IFilter>
        {
            [PdfTokens.NameToken.FlateDecode.Data] = flate,
            [PdfTokens.NameToken.FlateDecodeAbbreviation.Data] = flate,
            [PdfTokens.NameToken.DctDecode.Data] = new PdfFilters.DctDecodeFilter(),
            [PdfTokens.NameToken.DctDecodeAbbreviation.Data] = new PdfFilters.DctDecodeFilter(),
        })
    {
        public BoundedFlate Flate => flate;
    }


    private sealed class BoundedFlate(CancellationToken cancellationToken) : PdfFilters.IFilter
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
            if (++streamsDecoded > NativeFilePreview.MaxPdfStreams)
            {
                Refuse(NativePreviewRefusal.TooComplex);
            }

            var parameters = PdfFilters.DecodeParameterResolver.GetFilterParameters(streamDictionary, filterIndex);
            if (parameters.TryGet(PdfTokens.NameToken.Predictor, out var predictor)
                && predictor is not PdfTokens.NumericToken { Double: 1 })
            {
                Refuse(NativePreviewRefusal.TooComplex);
            }

            var limit = Math.Min(
                NativeFilePreview.MaxPdfDecodedStreamBytes,
                NativeFilePreview.MaxPdfTotalDecodedBytes - totalDecoded);
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
                    return result.GetBuffer().AsMemory(0, (int)result.Length);
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
            if (length > NativeFilePreview.MaxPdfTotalDecodedBytes - totalDecoded)
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
    }

    #endregion
}
