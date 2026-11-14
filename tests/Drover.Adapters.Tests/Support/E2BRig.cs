namespace Drover.Adapters.Tests.Support;

/// <summary>The E2B adapter wired to a fixture in Fixtures/e2b, with the handler kept for wire checks.</summary>
public sealed record E2BRigged(SandboxUnderTest Subject, RecordedHandler Handler);

public static class E2BRig
{
    #region Private Fields

    public const string Domain = "e2b.dev";

    public const string SandboxId = "fixturesandbox0000001";

    /// <summary>The fixtures' metadata carries these, so the metadata check passes for the rig's spec.</summary>
    public static readonly Guid SpaceId = Guid.Parse("0193a8f0-0000-7000-8000-000000000001");

    public static readonly Guid SharingGroupId = Guid.Parse("0193a8f0-0000-7000-8000-000000000002");

    public static readonly Guid IdempotencyKey = Guid.Parse("0193a8f0-0000-7000-8000-000000000003");

    #endregion

    #region Public Methods

    public static SandboxSpec Spec() =>
        new()
        {
            SpaceId = SpaceId,
            SharingGroupId = SharingGroupId,
            IdempotencyKey = IdempotencyKey,
            Network = new NetworkPolicy(NetworkAccess.Open, []),
            IdleTimeout = TimeSpan.FromMinutes(2),
        };

    public static E2BRigged Start(string fixtureName) => Start(RecordedFixture.Load("e2b", fixtureName));

    public static E2BRigged Start(RecordedFixture fixture)
    {
        var handler = new RecordedHandler(fixture);
        var provider = Provider(new FixtureHttpClientFactory(handler));

        return new E2BRigged(new SandboxUnderTest(provider, Spec(), "/home/user/probe", handler.Wire), handler);
    }

    public static E2BSandboxProvider Provider(IHttpClientFactory httpClients) =>
        new(
            httpClients,
            Options.Create(new E2BOptions { ApiKey = TestKeys.Generate(), Domain = Domain }),
            NullLogger<E2BSandboxProvider>.Instance);

    /// <summary>A handle for the fixtures' sandbox, as the broker would build it from a row.</summary>
    public static SandboxHandle Handle() => new(SandboxId, SpaceId, SharingGroupId, "fixture-envd-token");

    #endregion
}

/// <summary>
/// What the live run created and sent. Every sandbox it creates is tagged <c>drover.test</c> with this run's id, and
/// only the ids recorded here are destroyed afterwards. The account holds other sandboxes; nothing lists and kills.
/// </summary>
public sealed class LiveSandboxes(Guid tag)
{
    #region Private Fields

    private readonly List<string> created = [];

    private readonly List<WireRequest> sent = [];

    private readonly object gate = new();

    #endregion

    #region Properties

    public Guid Tag { get; } = tag;

    #endregion

    #region Public Methods

    public IReadOnlyList<string> Created()
    {
        lock (gate)
        {
            return [.. created];
        }
    }

    public IReadOnlyList<WireRequest> Sent()
    {
        lock (gate)
        {
            return [.. sent];
        }
    }

    public void RecordCreated(string sandboxId)
    {
        lock (gate)
        {
            created.Add(sandboxId);
        }
    }

    public void RecordSent(WireRequest request)
    {
        lock (gate)
        {
            sent.Add(request);
        }
    }

    #endregion
}

/// <summary>Sits in the live run's client pipelines: tags each creation, records each request, and keeps created ids.</summary>
public sealed class LiveTap(LiveSandboxes sandboxes) : DelegatingHandler
{
    #region Interface Implementations

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isJson = request.Content?.Headers.ContentType?.MediaType == "application/json";
        var body = isJson ? await request.Content!.ReadAsStringAsync(cancellationToken) : string.Empty;
        var isCreate = request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/v2/sandboxes";

        sandboxes.RecordSent(new WireRequest(request.Method.Method, request.RequestUri!, body));

        if (isCreate)
        {
            request.Content = new StringContent(Tagged(body), Encoding.UTF8, "application/json");
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (isCreate && response.IsSuccessStatusCode)
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            using var answer = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            sandboxes.RecordCreated(answer.RootElement.GetProperty("sandboxID").GetString()!);
        }

        return response;
    }

    #endregion

    #region Private Methods

    private string Tagged(string body)
    {
        var json = JsonNode.Parse(body)!.AsObject();
        json["metadata"]!["drover.test"] = sandboxes.Tag.ToString("D");

        return json.ToJsonString();
    }

    #endregion
}
