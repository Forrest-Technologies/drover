namespace Drover.Server.Tests;

/// <summary>
/// The real compatible adapter against a local web server: the key travels in the request, models are discovered
/// into the catalog, and the private-address rule of the deployment and of the space decides whether the request
/// is sent at all. Host names resolve through a test resolver, never real DNS.
/// </summary>
public abstract class ModelConnectionOutboundTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    protected const string PrivateName = "private-name.test";

    protected const string PublicName = "public-name.test";

    private const string ModelList = """{"data":[{"id":"local-small"},{"id":"local-large"}]}""";

    private static readonly IPAddress loopback = IPAddress.Loopback;

    private readonly Dictionary<string, IPAddress> names = new()
    {
        [PrivateName] = IPAddress.Loopback,
        [PublicName] = IPAddress.Parse("127.0.0.2"),
    };

    #endregion

    #region Properties

    protected abstract DeploymentPolicy Rules { get; }

    protected List<string> Requests { get; } = [];

    /// <summary>When set, only 127.0.0.1 counts as private, so a second loopback address stands in for a public one.</summary>
    protected bool OnlyTheFirstLoopbackIsPrivate { get; set; }

    protected string Body { get; set; } = ModelList;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(_ =>
            OutboundHttp.CreateHandler(
                Rules,
                (host, _) => Task.FromResult(new[] { names[host] }),
                address => OnlyTheFirstLoopbackIsPrivate ? address.Equals(loopback) : PrivateAddresses.Contains(address))));

    #endregion

    #region Helpers

    protected TestWebServer StartServer(Func<string, string>? answer = null) =>
        new(request =>
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return answer?.Invoke(request) ?? TestWebServer.Ok(Body);
        });

    protected async Task<(ApiClient Client, ModelConnectionSummary Connection, Guid SpaceId)> CreateConnection(
        string address,
        string? key = null)
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        var response = await client.Post(
            $"/api/spaces/{alice.SpaceId}/model-connections",
            new CreateModelConnectionRequest("openai_compatible", "Local", address, key, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));

        return (client, (await response.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!, alice.SpaceId);
    }

    protected static async Task<ConnectionTestResponse> Test(ApiClient client, Guid spaceId, Guid connectionId) =>
        (await (await client.Post($"/api/spaces/{spaceId}/model-connections/{connectionId}/test")).Content
            .ReadFromJsonAsync<ConnectionTestResponse>(Token))!;

    protected static async Task<ModelRefreshResponse> Refresh(ApiClient client, Guid spaceId, Guid connectionId)
    {
        var response = await client.Post($"/api/spaces/{spaceId}/model-connections/{connectionId}/models/refresh");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<ModelRefreshResponse>(Token))!;
    }

    protected async Task SetSpacePrivateAddresses(Guid spaceId, bool allowed)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(spaceId, Guid.CreateVersion7());
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var space = await provider.GetRequiredService<DroverDbContext>().Spaces.SingleAsync(row => row.Id == spaceId, Token);
        space.Settings = new SpaceSettings { AllowPrivateAddresses = allowed };
        await work.Commit(Token);
    }

    #endregion
}

public sealed class SelfHostedModelConnectionOutboundTests(PostgresServer postgres) : ModelConnectionOutboundTests(postgres)
{
    #region Properties

    protected override DeploymentPolicy Rules => DeploymentPolicy.SelfHosted;

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_compatible_server_on_the_local_network_is_tested_with_the_key_and_its_models_are_catalogued()
    {
        using var server = StartServer();
        var key = $"test-key-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";
        var (client, connection, spaceId) = await CreateConnection($"http://127.0.0.1:{server.Port}/v1", key);

        var test = await Test(client, spaceId, connection.Id);
        var refresh = await Refresh(client, spaceId, connection.Id);

        test.Succeeded.Should().BeTrue(test.Message);
        test.Connection.Status.Should().Be("ready");
        refresh.Models.Select(model => model.Model).Should().BeEquivalentTo(["local-small", "local-large"]);
        refresh.Models.Should().OnlyContain(model => model.Capabilities.ToolCalling && model.Capabilities.Streaming
            && !model.Capabilities.Vision && !model.Capabilities.Reasoning && !model.Capabilities.StructuredOutput
            && !model.Capabilities.WebSearch);
        lock (Requests)
        {
            Requests.Should().OnlyContain(request => request.Contains($"Bearer {key}", StringComparison.OrdinalIgnoreCase));
        }

        Logs.AllText().Should().NotContain(key);
    }

    [Fact]
    public async Task A_space_can_forbid_private_addresses_even_where_the_deployment_allows_them()
    {
        using var server = StartServer();
        var (client, connection, spaceId) = await CreateConnection($"http://{PrivateName}:{server.Port}/v1");
        await SetSpacePrivateAddresses(spaceId, allowed: false);

        var test = await Test(client, spaceId, connection.Id);

        test.Succeeded.Should().BeFalse();
        test.Failure.Should().Be("rejected");
        test.Message.Should().Contain("private addresses");
        server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_space_that_forbids_private_addresses_cannot_create_a_connection_to_a_literal_one()
    {
        var alice = await SeedPerson("alice");
        await SetSpacePrivateAddresses(alice.SpaceId, allowed: false);
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();

        var response = await client.Post(
            $"/api/spaces/{alice.SpaceId}/model-connections",
            new CreateModelConnectionRequest("openai_compatible", "Local", "http://10.1.2.3/v1", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("private addresses");
    }

    [Fact]
    public async Task A_model_list_that_is_not_json_is_reported_as_rejected_and_not_as_a_server_error()
    {
        Body = "<html>not a model list</html>";
        using var server = StartServer();
        var (client, connection, spaceId) = await CreateConnection($"http://127.0.0.1:{server.Port}/v1");

        var test = await Test(client, spaceId, connection.Id);
        var refresh = await Refresh(client, spaceId, connection.Id);

        test.Succeeded.Should().BeFalse();
        test.Failure.Should().Be("rejected");
        refresh.Succeeded.Should().BeFalse();
        refresh.Failure.Should().Be("rejected");
        refresh.Models.Should().BeEmpty();
    }

    #endregion
}

public sealed class HostedModelConnectionOutboundTests(PostgresServer postgres) : ModelConnectionOutboundTests(postgres)
{
    #region Properties

    protected override DeploymentPolicy Rules => DeploymentPolicy.Hosted;

    protected override IReadOnlyDictionary<string, string?> Settings
    {
        get
        {
            var settings = new Dictionary<string, string?>(base.Settings) { ["DROVER_POLICY"] = DeploymentPolicy.HostedName };

            foreach (var (key, value) in KeyRingCertificate.Settings)
            {
                settings[key] = value;
            }

            return settings;
        }
    }

    #endregion

    #region Public Methods

    [Fact]
    public async Task The_hosted_service_refuses_a_host_name_that_resolves_to_a_private_address()
    {
        using var server = StartServer();
        var (client, connection, spaceId) = await CreateConnection($"http://{PrivateName}:{server.Port}/v1");

        var test = await Test(client, spaceId, connection.Id);
        var refresh = await Refresh(client, spaceId, connection.Id);

        test.Succeeded.Should().BeFalse();
        test.Failure.Should().Be("rejected");
        test.Message.Should().Contain("private addresses");
        test.Connection.Status.Should().Be("failed");
        refresh.Succeeded.Should().BeFalse();
        refresh.Message.Should().Contain("private addresses");
        server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task The_hosted_service_refuses_a_literal_private_address_when_the_connection_is_created()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();

        var response = await client.Post(
            $"/api/spaces/{alice.SpaceId}/model-connections",
            new CreateModelConnectionRequest("openai_compatible", "Local", "http://127.0.0.1:8080/v1", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Token)).Should().Contain("private addresses");
    }

    [Fact]
    public async Task A_name_that_resolves_to_a_public_address_is_reached_at_the_address_that_was_checked()
    {
        OnlyTheFirstLoopbackIsPrivate = true;
        using var server = StartServer();
        var (client, connection, spaceId) = await CreateConnection($"http://{PublicName}:{server.Port}/v1");

        var test = await Test(client, spaceId, connection.Id);

        test.Succeeded.Should().BeTrue(test.Message);
    }

    [Fact]
    public async Task A_redirect_from_a_public_name_to_a_private_address_is_refused()
    {
        OnlyTheFirstLoopbackIsPrivate = true;
        using var server = StartServer(request => request.Contains(PublicName, StringComparison.Ordinal)
            ? TestWebServer.Redirect($"http://127.0.0.1:{PortOf(request)}/v1/models")
            : TestWebServer.Ok(Body));
        var (client, connection, spaceId) = await CreateConnection($"http://{PublicName}:{server.Port}/v1");

        var test = await Test(client, spaceId, connection.Id);

        test.Succeeded.Should().BeFalse();
        test.Message.Should().Contain("private addresses");
        lock (Requests)
        {
            Requests.Should().ContainSingle("only the redirecting request reached the server");
        }
    }

    [Fact]
    public async Task The_hosted_byte_cap_refuses_a_model_list_the_self_hosted_cap_would_accept()
    {
        OnlyTheFirstLoopbackIsPrivate = true;
        Body = $$"""{"data":[{"id":"big","pad":"{{new string('x', DeploymentPolicy.HostedMaxModelListBytes + 1024)}}"}]}""";
        using var server = StartServer();
        var (client, connection, spaceId) = await CreateConnection($"http://{PublicName}:{server.Port}/v1");

        var refresh = await Refresh(client, spaceId, connection.Id);

        refresh.Succeeded.Should().BeFalse();
        refresh.Failure.Should().Be("rejected");
        refresh.Message.Should().Contain("larger than this server accepts");
        refresh.Models.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static int PortOf(string request)
    {
        var host = request.Split("\r\n").First(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));

        return int.Parse(host[(host.LastIndexOf(':') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
    }

    #endregion
}

public sealed class SelfHostedModelListLimitTests(PostgresServer postgres) : ModelConnectionOutboundTests(postgres)
{
    #region Properties

    protected override DeploymentPolicy Rules => DeploymentPolicy.SelfHosted;

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:Policy:Rules:MaxModelListBytes"] = "2048",
    };

    #endregion

    #region Public Methods

    [Fact]
    public async Task An_operator_can_tighten_the_byte_cap_and_a_longer_list_is_refused_while_a_shorter_one_is_read()
    {
        using var server = StartServer();
        var (client, connection, spaceId) = await CreateConnection($"http://127.0.0.1:{server.Port}/v1");

        var small = await Refresh(client, spaceId, connection.Id);
        Body = $$"""{"data":[{"id":"big","pad":"{{new string('x', 4096)}}"}]}""";
        var large = await Refresh(client, spaceId, connection.Id);

        small.Succeeded.Should().BeTrue();
        large.Succeeded.Should().BeFalse();
        large.Message.Should().Contain("larger than this server accepts");
        large.Models.Select(model => model.Model).Should().BeEquivalentTo(["local-small", "local-large"], "a refused list leaves the catalog alone");
    }

    #endregion
}
