namespace Drover.Adapters.Tests.Support;

/// <summary>A request as the adapter sent it, kept so a test can look at the wire.</summary>
public sealed record SentRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body)
{
    /// <summary>The JSON body, or an empty object when the request had none.</summary>
    public JsonElement Json => string.IsNullOrEmpty(Body) ? default : JsonElement.Parse(Body);
}

/// <summary>
/// Serves a fixture instead of the network. Each exchange answers once, in order; when every match has been used,
/// the last one repeats, which is how a failing endpoint behaves.
/// </summary>
public sealed class RecordedHandler(RecordedFixture fixture) : HttpMessageHandler
{
    #region Private Fields

    private readonly List<RecordedExchange> unused = [.. fixture.Exchanges];

    private readonly object gate = new();

    #endregion

    #region Properties

    public List<SentRequest> Requests { get; } = [];

    #endregion

    #region Public Methods

    /// <summary>The requests seen so far, in the shape the conformance suite reads.</summary>
    public IReadOnlyList<WireRequest> Wire()
    {
        lock (gate)
        {
            return [.. Requests.Select(request => new WireRequest(request.Method.Method, request.Uri, request.Body))];
        }
    }

    #endregion

    #region Interface Implementations

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value));

        lock (gate)
        {
            Requests.Add(new SentRequest(request.Method, request.RequestUri!, headers, body));
        }

        var exchange = Take(request) ?? throw new InvalidOperationException(
            $"The fixture has no response for {request.Method} {request.RequestUri!.AbsolutePath}.");

        var response = new HttpResponseMessage((HttpStatusCode)exchange.Status)
        {
            Content = exchange.ContentType == "text/event-stream"
                ? new StreamContent(new NonSeekableStream(new MemoryStream(Encoding.UTF8.GetBytes(exchange.Body))))
                : new StringContent(exchange.Body, Encoding.UTF8, exchange.ContentType),
        };

        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(exchange.ContentType);

        foreach (var header in exchange.ResolveHeaders(DateTimeOffset.UtcNow))
        {
            response.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return response;
    }

    #endregion

    #region Private Methods

    private RecordedExchange? Take(HttpRequestMessage request)
    {
        lock (gate)
        {
            var next = unused.FirstOrDefault(exchange => exchange.Matches(request));

            if (next is not null)
            {
                unused.Remove(next);

                return next;
            }

            return fixture.Exchanges.LastOrDefault(exchange => exchange.Matches(request));
        }
    }

    #endregion
}
