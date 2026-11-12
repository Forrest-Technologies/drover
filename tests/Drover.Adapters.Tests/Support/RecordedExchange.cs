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

    #endregion

    #region Public Methods

    public bool Matches(HttpRequestMessage request) =>
        (Method == "*" || request.Method.Method == Method)
        && (Path == "*" || request.RequestUri!.AbsolutePath.EndsWith(Path, StringComparison.Ordinal));

    /// <summary>Header values may use {now_ms+N}: the Unix time in milliseconds, N milliseconds from now.</summary>
    public IEnumerable<KeyValuePair<string, string>> ResolveHeaders(DateTimeOffset now) =>
        Headers.Select(header => KeyValuePair.Create(header.Key, ResolveClock(header.Value, now)));

    public static RecordedExchange Parse(JsonElement element)
    {
        var sse = element.TryGetProperty("sse", out var lines) ? lines : (JsonElement?)null;
        var body = element.TryGetProperty("body", out var found) ? found : (JsonElement?)null;

        return new RecordedExchange
        {
            Method = element.TryGetProperty("method", out var method) ? method.GetString()! : "*",
            Path = element.GetProperty("path").GetString()!,
            Status = element.TryGetProperty("status", out var status) ? status.GetInt32() : 200,
            Headers = element.TryGetProperty("headers", out var headers)
                ? headers.EnumerateObject().ToDictionary(header => header.Name, header => header.Value.GetString()!)
                : [],
            ContentType = sse is not null ? "text/event-stream" : "application/json",
            Body = sse is { } events ? ToEventStream(events) : body is { } json ? Compact(json) : string.Empty,
        };
    }

    #endregion

    #region Private Methods

    // A server event carries one line of JSON. The fixture files are indented for people.
    private static string Compact(JsonElement json) => JsonSerializer.Serialize(json);

    private static string ToEventStream(JsonElement events) =>
        string.Concat(events.EnumerateArray().Select(@event =>
            $"data: {(@event.ValueKind == JsonValueKind.String ? @event.GetString() : Compact(@event))}\n\n"));

    private static string ResolveClock(string value, DateTimeOffset now) =>
        Regex.Replace(
            value,
            @"\{now_ms\+(\d+)\}",
            match => now.AddMilliseconds(double.Parse(match.Groups[1].Value)).ToUnixTimeMilliseconds().ToString());

    #endregion
}
