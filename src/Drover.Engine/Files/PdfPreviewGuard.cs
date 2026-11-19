namespace Drover.Engine.Files;

/// <summary>
/// Limits concurrent PDF previews and screens out some expensive files before parsing.
/// The parser's filter provider enforces the decoded byte budget.
/// </summary>
public static partial class PdfPreviewGuard
{
    #region Private Fields

    private const int MaxPagesInteriorNodes = 4;

    private static readonly TimeSpan defaultDeadline = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan gateWaitTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan userWindow = TimeSpan.FromMinutes(1);

    private static readonly SemaphoreSlim globalGate = new(
        NativeFilePreview.MaxConcurrentPdfPreviews,
        NativeFilePreview.MaxConcurrentPdfPreviews);

    private static readonly ConcurrentDictionary<Guid, UserPdfBudget> userBudgets = new();

    #endregion

    #region Public Methods

    public static async Task<NativeFilePreview> Run(
        byte[] content,
        Guid userId = default,
        TimeSpan? deadline = null,
        Action? onParse = null,
        TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        if (content.Length > NativeFilePreview.MaxPdfBytes)
        {
            return new(Refusal: NativePreviewRefusal.TooLarge);
        }

        var time = timeProvider ?? TimeProvider.System;
        if (!TryAcquireUserSlot(userId, time))
        {
            return new(Refusal: NativePreviewRefusal.TooComplex);
        }

        var effectiveDeadline = deadline ?? defaultDeadline;
        var waitBudget = effectiveDeadline < gateWaitTimeout ? effectiveDeadline : gateWaitTimeout;
        var acquiredGlobal = false;
        try
        {
            acquiredGlobal = await globalGate.WaitAsync(waitBudget, cancellationToken);
        }
        catch
        {
            ReleaseUserSlot(userId);
            throw;
        }

        if (!acquiredGlobal)
        {
            ReleaseUserSlot(userId);
            return new(Refusal: NativePreviewRefusal.TooComplex);
        }

        using var parseCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var parseToken = parseCancellation.Token;
        var parseTask = Task.Run(
            () =>
            {
                try
                {
                    onParse?.Invoke();
                    return DocumentReader.ReadPdfPreview(content, parseToken);
                }
                finally
                {
                    ReleaseUserSlot(userId);
                    globalGate.Release();
                }
            });

        var elapsed = Task.Delay(effectiveDeadline, time, parseToken);
        var completed = await Task.WhenAny(parseTask, elapsed);
        await parseCancellation.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return completed == parseTask ? await parseTask : new(Refusal: NativePreviewRefusal.TooComplex);
    }

    public static NativePreviewRefusal? ScreenStructure(byte[] content, CancellationToken cancellationToken)
    {
        if (content.Length > NativeFilePreview.MaxPdfBytes)
        {
            return NativePreviewRefusal.TooLarge;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var chars = new char[content.Length];
        for (var index = 0; index < content.Length; index++)
        {
            chars[index] = (char)content[index];
        }

        var rawText = new string(chars);
        var objectStreams = new StringBuilder();
        var streamRefusal = InspectStreams(content, rawText, chars, objectStreams, cancellationToken);
        if (streamRefusal is not null)
        {
            return streamRefusal;
        }

        var structure = objectStreams.Length == 0
            ? new string(chars)
            : $"{new string(chars)}\n{objectStreams}";
        return InspectObjectsAndPageTree(structure, cancellationToken);
    }

    #endregion

    #region Private Methods

    private static NativePreviewRefusal? InspectStreams(
        byte[] content,
        string rawText,
        char[] strippedChars,
        StringBuilder objectStreams,
        CancellationToken cancellationToken)
    {
        var searchIndex = 0;
        var streamCount = 0;
        var totalDecodedBytes = 0;

        while (searchIndex < rawText.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keywordIndex = rawText.IndexOf("stream", searchIndex, StringComparison.Ordinal);
            if (keywordIndex < 0)
            {
                break;
            }

            if (keywordIndex >= 3 && rawText.AsSpan(keywordIndex - 3, 9).SequenceEqual("endstream"))
            {
                searchIndex = keywordIndex + 6;
                continue;
            }

            if (!TryGetStreamDataStart(rawText, keywordIndex, out var dataStart))
            {
                searchIndex = keywordIndex + 6;
                continue;
            }

            var dictionary = FindPrecedingDictionary(rawText, keywordIndex);
            if (dictionary is null)
            {
                searchIndex = keywordIndex + 6;
                continue;
            }

            var endStreamIndex = FindEndStream(rawText, dataStart);
            if (endStreamIndex < 0)
            {
                return NativePreviewRefusal.Malformed;
            }

            streamCount++;
            if (streamCount > NativeFilePreview.MaxPdfStreams)
            {
                return NativePreviewRefusal.TooComplex;
            }

            var dataEnd = TrimStreamTrailingNewline(rawText, dataStart, endStreamIndex);
            Array.Fill(strippedChars, ' ', dataStart, dataEnd - dataStart);

            var slice = content.AsSpan(dataStart, dataEnd - dataStart);
            if (TypeObjStmRegex().IsMatch(dictionary))
            {
                var objectRefusal = InspectObjectStream(
                    dictionary, slice, ref totalDecodedBytes, objectStreams, cancellationToken);
                if (objectRefusal is not null)
                {
                    return objectRefusal;
                }
            }

            searchIndex = endStreamIndex + "endstream".Length;
        }

        return null;
    }

    private static bool TryGetStreamDataStart(string text, int keywordIndex, out int dataStart)
    {
        dataStart = 0;
        var after = keywordIndex + "stream".Length;
        if (after >= text.Length)
        {
            return false;
        }

        if (text[after] == '\n')
        {
            dataStart = after + 1;
            return true;
        }

        if (text[after] == '\r' && after + 1 < text.Length && text[after + 1] == '\n')
        {
            dataStart = after + 2;
            return true;
        }

        return false;
    }

    private static string? FindPrecedingDictionary(string text, int streamKeywordIndex)
    {
        var cursor = streamKeywordIndex - 1;
        while (cursor >= 0 && char.IsWhiteSpace(text[cursor]))
        {
            cursor--;
        }

        if (cursor < 1 || text[cursor] != '>' || text[cursor - 1] != '>')
        {
            return null;
        }

        var depth = 0;
        for (var index = cursor; index >= 1; index--)
        {
            if (text[index] == '>' && text[index - 1] == '>')
            {
                depth++;
                index--;
            }
            else if (text[index] == '<' && text[index - 1] == '<')
            {
                depth--;
                index--;
                if (depth == 0)
                {
                    return text[index..(cursor + 1)];
                }
            }
        }

        return null;
    }

    private static int FindEndStream(string text, int dataStart)
    {
        var search = dataStart;
        while (search < text.Length)
        {
            var found = text.IndexOf("endstream", search, StringComparison.Ordinal);
            if (found < 0)
            {
                return -1;
            }

            var after = found + "endstream".Length;
            if (after >= text.Length || char.IsWhiteSpace(text[after]) || text[after] is '/' or '<' or '>' or '[')
            {
                return found;
            }

            search = found + 1;
        }

        return -1;
    }

    private static int TrimStreamTrailingNewline(string text, int dataStart, int endStreamIndex)
    {
        var end = endStreamIndex;
        if (end > dataStart && text[end - 1] == '\n')
        {
            end--;
        }

        if (end > dataStart && text[end - 1] == '\r')
        {
            end--;
        }

        return end;
    }

    // Only for early page-tree screening. The parser handles filter lookup and enforces its own byte budget.
    private static NativePreviewRefusal? InspectObjectStream(
        string dictionary,
        ReadOnlySpan<byte> compressed,
        ref int totalDecodedBytes,
        StringBuilder objectStreams,
        CancellationToken cancellationToken)
    {
        var filter = SingleFilterRegex().Match(dictionary);
        if (!filter.Success || filter.Groups[1].Value is not ("FlateDecode" or "Fl"))
        {
            return null;
        }

        if (compressed.IsEmpty)
        {
            return null;
        }

        var rawBytes = compressed.ToArray();
        using var source = new MemoryStream(rawBytes, writable: false);
        using Stream decompressor = HasZlibHeader(compressed)
            ? new ZLibStream(source, CompressionMode.Decompress)
            : new DeflateStream(source, CompressionMode.Decompress);

        var scratch = new byte[8192];
        var streamDecoded = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = decompressor.Read(scratch, 0, scratch.Length);
            if (read <= 0)
            {
                break;
            }

            streamDecoded += read;
            totalDecodedBytes += read;
            if (streamDecoded > NativeFilePreview.MaxPdfDecodedStreamBytes
                || totalDecodedBytes > NativeFilePreview.MaxPdfTotalDecodedBytes)
            {
                return NativePreviewRefusal.TooLarge;
            }

            for (var index = 0; index < read; index++)
            {
                objectStreams.Append((char)scratch[index]);
            }
        }

        objectStreams.Append('\n');
        return null;
    }

    private static bool HasZlibHeader(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 2
        && (bytes[0] & 0x0F) == 8
        && (bytes[0] >> 4) <= 7
        && (((bytes[0] << 8) | bytes[1]) % 31) == 0;

    private static NativePreviewRefusal? InspectObjectsAndPageTree(
        string structure, CancellationToken cancellationToken)
    {
        var objects = IndirectObjectRegex().Matches(structure);
        if (objects.Count > NativeFilePreview.MaxPdfObjects)
        {
            return NativePreviewRefusal.TooComplex;
        }

        var pagesNodes = 0;
        var pageLeaves = 0;
        var seenKids = new HashSet<int>();
        var edges = new Dictionary<int, HashSet<int>>();

        foreach (Match match in objects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var objectId))
            {
                continue;
            }

            var body = match.Groups[2].Value;
            var isPagesNode = PagesTypeRegex().IsMatch(body);
            var isPageLeaf = !isPagesNode && PageTypeRegex().IsMatch(body);

            if (isPagesNode)
            {
                pagesNodes++;
                if (pagesNodes > MaxPagesInteriorNodes)
                {
                    return NativePreviewRefusal.TooComplex;
                }

                if (TryReadIntParameter(CountRegex(), body, out var count))
                {
                    if (count <= 0)
                    {
                        return NativePreviewRefusal.Malformed;
                    }

                    if (count > NativeFilePreview.MaxPdfPages)
                    {
                        return NativePreviewRefusal.TooLarge;
                    }
                }

                var kidsMatch = KidsRegex().Match(body);
                if (kidsMatch.Success)
                {
                    var references = IndirectReferenceRegex().Matches(kidsMatch.Groups[1].Value);
                    if (references.Count == 0)
                    {
                        return NativePreviewRefusal.Malformed;
                    }

                    foreach (Match reference in references)
                    {
                        var childId = int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture);
                        if (childId == objectId || !seenKids.Add(childId))
                        {
                            return NativePreviewRefusal.TooComplex;
                        }

                        if (seenKids.Count > NativeFilePreview.MaxPdfPages + MaxPagesInteriorNodes)
                        {
                            return NativePreviewRefusal.TooComplex;
                        }

                        AddEdge(edges, objectId, childId);
                    }
                }
            }
            else if (isPageLeaf)
            {
                pageLeaves++;
                if (pageLeaves > NativeFilePreview.MaxPdfPages)
                {
                    return NativePreviewRefusal.TooLarge;
                }
            }

            if ((isPagesNode || isPageLeaf) && TryReadIntParameter(ParentRegex(), body, out var parentId))
            {
                if (parentId == objectId)
                {
                    return NativePreviewRefusal.TooComplex;
                }

                AddEdge(edges, parentId, objectId);
            }
        }

        return HasCycle(edges) ? NativePreviewRefusal.TooComplex : null;
    }

    private static bool TryReadIntParameter(Regex regex, string input, out int value)
    {
        var match = regex.Match(input);
        if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = 0;
        return false;
    }

    private static void AddEdge(Dictionary<int, HashSet<int>> edges, int from, int to)
    {
        if (!edges.TryGetValue(from, out var targets))
        {
            targets = [];
            edges[from] = targets;
        }

        targets.Add(to);
    }

    private static bool HasCycle(Dictionary<int, HashSet<int>> edges)
    {
        var state = new Dictionary<int, int>();
        foreach (var node in edges.Keys)
        {
            if (Visit(node, edges, state))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Visit(int node, Dictionary<int, HashSet<int>> edges, Dictionary<int, int> state)
    {
        if (state.TryGetValue(node, out var current))
        {
            return current == 1;
        }

        state[node] = 1;
        if (edges.TryGetValue(node, out var targets))
        {
            foreach (var target in targets)
            {
                if (Visit(target, edges, state))
                {
                    return true;
                }
            }
        }

        state[node] = 2;
        return false;
    }

    private static bool TryAcquireUserSlot(Guid userId, TimeProvider time)
    {
        if (userId == Guid.Empty)
        {
            return true;
        }

        var budget = userBudgets.GetOrAdd(userId, _ => new UserPdfBudget());
        var now = time.GetUtcNow();
        lock (budget.Sync)
        {
            while (budget.RecentStarts.Count > 0 && now - budget.RecentStarts.Peek() >= userWindow)
            {
                budget.RecentStarts.Dequeue();
            }

            if (budget.Active >= 1 || budget.RecentStarts.Count >= NativeFilePreview.MaxPdfPreviewsPerUserPerMinute)
            {
                return false;
            }

            budget.Active++;
            budget.RecentStarts.Enqueue(now);
            return true;
        }
    }

    private static void ReleaseUserSlot(Guid userId)
    {
        if (userId == Guid.Empty || !userBudgets.TryGetValue(userId, out var budget))
        {
            return;
        }

        lock (budget.Sync)
        {
            if (budget.Active > 0)
            {
                budget.Active--;
            }
        }
    }

    [GeneratedRegex(@"/Type\s*/ObjStm\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex TypeObjStmRegex();

    [GeneratedRegex(@"/Filter\s*/([A-Za-z0-9]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SingleFilterRegex();

    [GeneratedRegex(@"(?:^|\s)(\d+)\s+\d+\s+obj\b([\s\S]*?)endobj\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex IndirectObjectRegex();

    [GeneratedRegex(@"/Type\s*/Pages\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PagesTypeRegex();

    [GeneratedRegex(@"/Type\s*/Page\b(?!s)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex PageTypeRegex();

    [GeneratedRegex(@"/Count\s+(-?\d+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CountRegex();

    [GeneratedRegex(@"/Kids\s*\[([^\]]*)\]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex KidsRegex();

    [GeneratedRegex(@"/Parent\s+(\d+)\s+\d+\s+R\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ParentRegex();

    [GeneratedRegex(@"(\d+)\s+\d+\s+R\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex IndirectReferenceRegex();

    #endregion

    #region Nested Types

    private sealed class UserPdfBudget
    {
        public object Sync { get; } = new();

        public int Active { get; set; }

        public Queue<DateTimeOffset> RecentStarts { get; } = new();
    }

    #endregion
}
