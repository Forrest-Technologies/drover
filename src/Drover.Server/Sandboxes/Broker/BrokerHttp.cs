namespace Drover.Server.Sandboxes.Broker;

/// <summary>The start of an HTTP/1.1 request: its request line and headers, in the order sent.</summary>
public sealed record BrokerRequestHead(string Method, string Target, string Version, IReadOnlyList<KeyValuePair<string, string>> Headers)
{
    #region Public Methods

    /// <summary>The headers with this name, compared without case.</summary>
    public IEnumerable<string> All(string name) =>
        Headers.Where(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Select(header => header.Value);

    public string? Only(string name) => All(name).ToList() is [var only] ? only : null;

    public bool Has(string name) => All(name).Any();

    #endregion
}


/// <summary>
/// Reads HTTP/1.1 from a stream the broker serves: request heads, and bodies by length or in chunks, each bounded.
/// Anything it cannot read exactly as HTTP/1.1 is a <see cref="BrokerHttpException"/>, answered with 400 and the
/// connection closed, so a request can never be read two ways.
/// </summary>
public sealed class BrokerHttpReader(Stream stream)
{
    #region Private Fields

    public const int MaxHeadBytes = 64 * 1024;

    private readonly byte[] buffer = new byte[16 * 1024];

    private int start;

    private int end;

    #endregion

    #region Public Methods

    /// <summary>The next request head, or null when the other side closed before sending one.</summary>
    public async Task<BrokerRequestHead?> ReadHead(CancellationToken cancellationToken)
    {
        var head = new List<byte>();

        while (true)
        {
            if (start == end && !await Fill(cancellationToken))
            {
                return head.Count == 0 ? null : throw new BrokerHttpException("The request ended in its head.");
            }

            while (start < end)
            {
                head.Add(buffer[start++]);

                if (head.Count >= 4 && head[^1] == '\n' && head[^2] == '\r' && head[^3] == '\n' && head[^4] == '\r')
                {
                    return Parse(Encoding.Latin1.GetString(CollectionsMarshal.AsSpan(head)[..^4]));
                }

                if (head.Count > MaxHeadBytes)
                {
                    throw new BrokerHttpException("The request's head is too large.");
                }
            }
        }
    }

    /// <summary>The body the head announces, read whole, at most <paramref name="limit"/> bytes.</summary>
    public async Task<byte[]> ReadBody(BrokerRequestHead head, int limit, CancellationToken cancellationToken)
    {
        var transfer = head.All("Transfer-Encoding").ToList();
        var lengths = head.All("Content-Length").ToList();

        if (transfer.Count > 0 && lengths.Count > 0)
        {
            throw new BrokerHttpException("A request may not say both how long it is and that it comes in chunks.");
        }

        if (transfer.Count > 0)
        {
            return transfer is [var only] && string.Equals(only.Trim(), "chunked", StringComparison.OrdinalIgnoreCase)
                ? await ReadChunked(limit, cancellationToken)
                : throw new BrokerHttpException("Only chunked transfer encoding is read.");
        }

        if (lengths.Count == 0)
        {
            return [];
        }

        if (lengths.Distinct(StringComparer.Ordinal).Count() != 1 || !long.TryParse(lengths[0], NumberStyles.None, CultureInfo.InvariantCulture, out var length))
        {
            throw new BrokerHttpException("The request's length cannot be read.");
        }

        return length > limit ? throw new BrokerTooLargeException() : await ReadExactly((int)length, cancellationToken);
    }

    /// <summary>Bytes already read past the head, which belong to whatever follows it.</summary>
    public ReadOnlyMemory<byte> Remaining() => buffer.AsMemory(start, end - start);

    #endregion

    #region Private Methods

    private static BrokerRequestHead Parse(string text)
    {
        var lines = text.Split("\r\n");
        var request = lines[0].Split(' ');

        if (lines[0].Any(letter => letter is '\r' or '\n' or '\0' or '\t'))
        {
            throw new BrokerHttpException("The request line cannot be read.");
        }


        if (request is not [var method, var target, var version] || method.Length == 0 || target.Length == 0 || version is not ("HTTP/1.1" or "HTTP/1.0"))
        {
            throw new BrokerHttpException("The request line cannot be read.");
        }

        List<KeyValuePair<string, string>> headers = [];

        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);

            // A line folded onto the one before, or a name with space or control characters in it, could be read two
            // ways by two servers. It is refused.
            if (colon <= 0 || line[0] is ' ' or '\t' || line[..colon].Any(letter => letter <= ' ' || letter >= 127) || line.Any(letter => letter is '\r' or '\n' or '\0'))
            {
                throw new BrokerHttpException("A request header cannot be read.");
            }

            headers.Add(new(line[..colon], line[(colon + 1)..].Trim(' ', '\t')));
        }

        return new BrokerRequestHead(method, target, version, headers);
    }

    private async Task<byte[]> ReadChunked(int limit, CancellationToken cancellationToken)
    {
        var body = new MemoryStream();

        while (true)
        {
            var sizeLine = await ReadLine(cancellationToken);
            var size = sizeLine.Split(';')[0].Trim();

            if (!int.TryParse(size, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var length) || length < 0)
            {
                throw new BrokerHttpException("A chunk's size cannot be read.");
            }

            if (length == 0)
            {
                while ((await ReadLine(cancellationToken)).Length > 0)
                {
                }

                return body.ToArray();
            }

            if (body.Length + length > limit)
            {
                throw new BrokerTooLargeException();
            }

            body.Write(await ReadExactly(length, cancellationToken));

            if ((await ReadLine(cancellationToken)).Length != 0)
            {
                throw new BrokerHttpException("A chunk did not end where its size said.");
            }
        }
    }

    private async Task<string> ReadLine(CancellationToken cancellationToken)
    {
        var line = new List<byte>();

        while (true)
        {
            if (start == end && !await Fill(cancellationToken))
            {
                throw new BrokerHttpException("The request ended early.");
            }

            var letter = buffer[start++];

            if (letter == '\n')
            {
                return line.Count > 0 && line[^1] == '\r'
                    ? Encoding.Latin1.GetString(CollectionsMarshal.AsSpan(line)[..^1])
                    : throw new BrokerHttpException("A line did not end with a carriage return.");
            }

            line.Add(letter);

            if (line.Count > 4096)
            {
                throw new BrokerHttpException("A line is too long.");
            }
        }
    }

    private async Task<byte[]> ReadExactly(int length, CancellationToken cancellationToken)
    {
        var bytes = new byte[length];
        var read = 0;

        while (read < length)
        {
            if (start == end && !await Fill(cancellationToken))
            {
                throw new BrokerHttpException("The request ended early.");
            }

            var take = Math.Min(length - read, end - start);
            buffer.AsSpan(start, take).CopyTo(bytes.AsSpan(read));
            start += take;
            read += take;
        }

        return bytes;
    }

    private async Task<bool> Fill(CancellationToken cancellationToken)
    {
        start = 0;
        end = await stream.ReadAsync(buffer, cancellationToken);
        return end > 0;
    }

    #endregion
}


/// <summary>A request the broker cannot read as HTTP/1.1. The message is safe to send back: it never holds request text.</summary>
public class BrokerHttpException(string message) : Exception(message);


/// <summary>A request body larger than the broker reads.</summary>
public sealed class BrokerTooLargeException() : BrokerHttpException("The request's body is larger than the broker reads.");
