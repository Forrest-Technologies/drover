namespace Drover.Adapters.Tests;

/// <summary>
/// Runs the sandbox suite against the real E2B service with a key in DROVER_TEST_E2B_KEY, on the domain in
/// DROVER_TEST_E2B_DOMAIN (default e2b.app), with the public <c>base</c> template. The theory is explicit and carries
/// the Live trait, so the normal run and pull request CI never execute it. Run it with:
/// <c>DROVER_TEST_E2B_KEY=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-trait "Live=true"</c>.
/// Each scenario creates one small sandbox with a two-minute time to live, tagged <c>drover.test</c>, and destroys
/// exactly the sandboxes it created when it ends, pass or fail.
/// </summary>
[Trait("Live", "true")]
public sealed class E2BLiveConformanceTests : SandboxProviderConformance, IAsyncDisposable
{
    #region Private Fields

    private const string KeyVariable = "DROVER_TEST_E2B_KEY";

    private const string DomainVariable = "DROVER_TEST_E2B_DOMAIN";

    private readonly LiveSandboxes sandboxes = new(Guid.NewGuid());

    private ServiceProvider? services;

    #endregion

    #region Public Methods

    [Theory(Explicit = true)]
    [MemberData(nameof(Scenarios), MemberType = typeof(SandboxProviderConformance))]
    public Task Passes_against_the_live_service(string scenario) => RunScenario(scenario);

    public async ValueTask DisposeAsync()
    {
        if (services is null)
        {
            return;
        }

        var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(E2BSandboxProvider.ApiClientName);

        foreach (var sandboxId in sandboxes.Created())
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"https://api.{ReadDomain()}/sandboxes/{sandboxId}"));
            request.Headers.Add("X-API-Key", ReadKey());
            using var response = await client.SendAsync(request, CancellationToken.None);
            Note($"Cleanup of sandbox {sandboxId}: HTTP {(int)response.StatusCode}");
        }

        await services.DisposeAsync();
    }

    #endregion

    #region Adapter Hooks

    protected override SandboxUnderTest Start(string scenario)
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddE2BSandboxProvider(options =>
        {
            options.ApiKey = ReadKey();
            options.Domain = ReadDomain();
        });
        collection.AddHttpClient(E2BSandboxProvider.ApiClientName).AddHttpMessageHandler(() => new LiveTap(sandboxes));
        collection.AddHttpClient(E2BSandboxProvider.EnvdClientName).AddHttpMessageHandler(() => new LiveTap(sandboxes));

        services = collection.BuildServiceProvider();
        Note($"Scenario {scenario}, drover.test={sandboxes.Tag}");

        var spec = new SandboxSpec
        {
            SpaceId = Guid.NewGuid(),
            SharingGroupId = Guid.NewGuid(),
            IdempotencyKey = Guid.NewGuid(),
            Network = new NetworkPolicy(NetworkAccess.Open, []),
            IdleTimeout = TimeSpan.FromMinutes(2),
        };

        return new SandboxUnderTest(services.GetRequiredService<ISandboxProvider>(), spec, "/home/user/drover-conformance", sandboxes.Sent);
    }

    #endregion

    #region Private Methods

    private static string ReadKey() =>
        Environment.GetEnvironmentVariable(KeyVariable)
        ?? throw new InvalidOperationException($"Set {KeyVariable} to an E2B API key to run the live sandbox tests.");

    private static string ReadDomain() => Environment.GetEnvironmentVariable(DomainVariable) ?? "e2b.app";

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion
}
