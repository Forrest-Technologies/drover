namespace Drover.Adapters.Tests.Support;

/// <summary>One scripted answer of the provider.</summary>
public sealed record ScriptedAnswer(HttpStatusCode Status, string ContentType, string Body)
{
    public static ScriptedAnswer Json(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status, "application/json", JsonSerializer.Serialize(body));

    public static ScriptedAnswer Raw(string body, HttpStatusCode status) => new(status, "text/html", body);

    public static ScriptedAnswer Stream(params object[] chunks) =>
        new(HttpStatusCode.OK, "text/event-stream", string.Concat(chunks.Select(chunk => $"data: {JsonSerializer.Serialize(chunk)}\n\n")));

    /// <summary>A reply whose one candidate holds the given parts.</summary>
    public static ScriptedAnswer Reply(object[] parts, string finish = "STOP", object? usage = null) =>
        Json(Chunk(parts, finish, usage));

    public static object Chunk(object[] parts, string? finish = "STOP", object? usage = null) => new
    {
        candidates = new[] { new { content = new { role = "model", parts }, finishReason = finish, index = 0 } },
        usageMetadata = usage,
        modelVersion = "gemini-test-thinker",
        responseId = "resp_script",
    };
}

/// <summary>Answers each request with the next scripted answer, in order, and keeps what was sent.</summary>
public sealed class GeminiScript
{
    #region Private Fields

    private readonly ScriptedAnswer[] answers;

    #endregion

    #region Constructors

    public GeminiScript(params ScriptedAnswer[] answers)
    {
        this.answers = answers;
        Handler = new ScriptedHandler(Answer);
    }

    #endregion

    #region Properties

    public const string Model = "gemini-test-thinker";

    public ScriptedHandler Handler { get; }

    public string Key { get; } = TestKeys.Generate();

    public List<SentRequest> Sent { get; } = [];

    public GeminiProvider Provider => new(new FixtureHttpClientFactory(Handler));

    public static ModelEndpoint Connection { get; } = new(GeminiProvider.ConnectionKind, GeminiProvider.BaseAddress);

    #endregion

    #region Public Methods

    public IModelChatClient Client(ModelSettings? settings = null) =>
        Provider.CreateChatClient(Connection, Key, Model, settings ?? new ModelSettings());

    #endregion

    #region Private Methods

    private async Task<HttpResponseMessage> Answer(HttpRequestMessage request, int count, CancellationToken cancellation)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellation);
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(',', header.Value));
        Sent.Add(new SentRequest(request.Method, request.RequestUri!, headers, body));
        var answer = answers[Math.Min(count, answers.Length) - 1];
        HttpContent content = answer.ContentType == "text/event-stream"
            ? new StreamContent(new NonSeekableStream(new MemoryStream(Encoding.UTF8.GetBytes(answer.Body))))
            : new StringContent(answer.Body, Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(answer.ContentType);

        return new HttpResponseMessage(answer.Status) { Content = content };
    }

    #endregion
}
