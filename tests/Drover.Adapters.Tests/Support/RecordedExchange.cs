namespace Drover.Adapters.Tests.Support;

/// <summary>One canned response. It answers requests whose method and path suffix match.</summary>
public sealed class RecordedExchange
{
    #region Properties

    /// <summary>"*" matches any method.</summary>
    public required string Method { get; init; }

    /// <summary>Matched against the end of the request path. "*" matches any path.</summary>
    public required string Path { get; init; }

    public required int Status { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public required string ContentType { get; init; }

    public required string Body { get; init; }

    /// <summary>A binary body, served as a stream: Connect frames or file bytes. Null for a text body.</summary>
    public byte[]? Bytes { get; init; }

    #endregion

    #region Public Methods

    public bool Matches(HttpRequestMessage request) =>
        (Method == "*" || request.Method.Method == Method)
        && (Path == "*" || request.RequestUri!.AbsolutePath.EndsWith(Path, StringComparison.Ordinal));

    /// <summary>Header values may use {now_ms+N}: the Unix time in milliseconds, N milliseconds from now.</summary>
    public IEnumerable<KeyValuePair<string, string>> ResolveHeaders(DateTimeOffset now) =>
        Headers.Select(header => KeyValuePair.Create(header.Key, ResolveClock(header.Value, now)));

    /// <summary>
    /// A body is one of: <c>body</c> (JSON), <c>sse</c> (server events), <c>connect</c> (Connect stream frames:
    /// <c>{"messages":[...],"end":{...}}</c>, where a missing <c>end</c> is a stream that stops without its end frame),
    /// or <c>bytes_base64</c> (an octet stream).
    /// </summary>
    public static RecordedExchange Parse(JsonElement element)
    {
        var sse = element.TryGetProperty("sse", out var lines) ? lines : (JsonElement?)null;
        var body = element.TryGetProperty("body", out var found) ? found : (JsonElement?)null;
        var connect = element.TryGetProperty("connect", out var frames) ? frames : (JsonElement?)null;
        var bytes = element.TryGetProperty("bytes_base64", out var encoded) ? encoded.GetBytesFromBase64() : null;

        return new RecordedExchange
        {
            Method = element.TryGetProperty("method", out var method) ? method.GetString()! : "*",
            Path = element.GetProperty("path").GetString()!,
            Status = element.TryGetProperty("status", out var status) ? status.GetInt32() : 200,
            Headers = element.TryGetProperty("headers", out var headers)
                ? headers.EnumerateObject().ToDictionary(header => header.Name, header => header.Value.GetString()!)
                : [],
            ContentType = (sse, connect, bytes) switch
            {
                ({ }, _, _) => "text/event-stream",
                (_, { }, _) => "application/connect+json",
                (_, _, { }) => "application/octet-stream",
                _ => "application/json",
            },
            Body = sse is { } events ? ToEventStream(events) : body is { } json ? Compact(json) : string.Empty,
            Bytes = connect is { } stream ? ToConnectStream(stream) : bytes,
        };
    }

    #endregion

    #region Private Methods

    // A server event carries one line of JSON. The fixture files are indented for people.
    private static string Compact(JsonElement json) => JsonSerializer.Serialize(json);

    private static string ToEventStream(JsonElement events) =>
        string.Concat(events.EnumerateArray().Select(@event =>
            $"data: {(@event.ValueKind == JsonValueKind.String ? @event.GetString() : Compact(@event))}\n\n"));

    /// <summary>Connect framing: a flag byte (2 marks the end of the stream), a big-endian length, then the JSON.</summary>
    private static byte[] ToConnectStream(JsonElement stream)
    {
        using var buffer = new MemoryStream();

        foreach (var message in stream.GetProperty("messages").EnumerateArray())
        {
            WriteFrame(buffer, 0, message);
        }

        if (stream.TryGetProperty("end", out var end))
        {
            WriteFrame(buffer, 2, end);
        }

        return buffer.ToArray();
    }

    private static void WriteFrame(MemoryStream buffer, byte flags, JsonElement message)
    {
        var json = Encoding.UTF8.GetBytes(Compact(message));
        var header = new byte[5];
        header[0] = flags;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), json.Length);
        buffer.Write(header);
        buffer.Write(json);
    }

    private static string ResolveClock(string value, DateTimeOffset now) =>
        Regex.Replace(
            value,
            @"\{now_ms\+(\d+)\}",
            match => now.AddMilliseconds(double.Parse(match.Groups[1].Value)).ToUnixTimeMilliseconds().ToString());

    #endregion
}
