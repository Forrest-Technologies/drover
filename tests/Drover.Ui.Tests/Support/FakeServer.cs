namespace Drover.Ui.Tests.Support;

/// <summary>
/// Stands in for the server. Each test says what an address answers; every request made is kept so a test
/// can assert what the screen sent. An address with no answer is a 404, as on the real server.
/// </summary>
public sealed class FakeServer : HttpMessageHandler
{
    #region Private Fields

    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

    private const string ClientName = "fake-server";

    private readonly Dictionary<string, Func<HttpResponseMessage>> answers = [];

    private ServiceProvider? clients;

    #endregion

    #region Properties

    public List<(string Method, string Path, string Body)> Requests { get; } = [];

    /// <summary>When set, every request fails as it does when the server cannot be reached.</summary>
    public bool Unreachable { get; set; }

    #endregion

    #region Public Methods

    public FakeServer Answer<T>(string method, string path, T body, HttpStatusCode status = HttpStatusCode.OK)
    {
        answers[$"{method} {path}"] = () => new(status) { Content = JsonContent.Create(body, options: json) };
        return this;
    }

    public FakeServer Answer(string method, string path, HttpStatusCode status)
    {
        answers[$"{method} {path}"] = () => new(status);
        return this;
    }

    /// <summary>Answers the way the server does when it refuses: a problem document with a sentence as its title.</summary>
    public FakeServer Refuse(string method, string path, HttpStatusCode status, string title)
    {
        answers[$"{method} {path}"] = () => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { title, status = (int)status }), Encoding.UTF8, "application/problem+json"),
        };
        return this;
    }

    /// <summary>
    /// Answers the address with an event stream the test writes to while the screen reads it. The stream stays
    /// open until the test ends it, as a run's stream does while the run is working.
    /// </summary>
    public LiveEvents Stream(string path)
    {
        var live = new LiveEvents();
        answers[$"GET {path}"] = () => new(HttpStatusCode.OK) { Content = live.ToContent() };
        return live;
    }

    public IEnumerable<string> Sent(string method, string path) =>
        Requests.Where(request => request.Method == method && request.Path == path).Select(request => request.Body);

    /// <summary>
    /// A client whose every request lands here. It comes from the client factory, as every client in Drover
    /// does, with this fake as the one handler behind it.
    /// </summary>
    public DroverApi CreateApi()
    {
        clients ??= new ServiceCollection()
            .AddHttpClient(ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => this)
            .Services
            .BuildServiceProvider();
        var client = clients.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);
        client.BaseAddress = new("https://drover.test/");
        return new(client);
    }

    #endregion

    #region Interface Implementations

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            clients?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, body));

        if (Unreachable)
        {
            throw new HttpRequestException("The server cannot be reached.");
        }

        return answers.TryGetValue($"{request.Method.Method} {request.RequestUri.AbsolutePath}", out var answer)
            ? answer()
            : new(HttpStatusCode.NotFound);
    }

    #endregion
}
