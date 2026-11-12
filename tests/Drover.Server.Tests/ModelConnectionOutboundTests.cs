namespace Drover.Server.Tests;

/// <summary>
/// The real compatible adapter against a local web server: the key travels in the request, models are discovered
/// into the catalog, and the private-address rule of the deployment and of the space decides whether the request
/// is sent at all.
/// </summary>
public abstract class ModelConnectionOutboundTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string ModelList = """{"data":[{"id":"local-small"},{"id":"local-large"}]}""";

    protected List<string> Requests { get; } = [];

    #endregion

    #region Helpers

    protected TestWebServer StartServer()
    {
        return new TestWebServer(request =>
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return TestWebServer.Ok(ModelList);
        });
    }

    protected async Task<(ApiClient Client, ModelConnectionSummary Connection, Guid SpaceId)> CreateLocalConnection(TestWebServer server, string? key)
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        var response = await client.Post(
            $"/api/spaces/{alice.SpaceId}/model-connections",
            new CreateModelConnectionRequest("openai_compatible", "Local", $"http://127.0.0.1:{server.Port}/v1", key, null));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (client, (await response.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!, alice.SpaceId);
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
    #region Public Methods

    [Fact]
    public async Task A_compatible_server_on_the_local_network_is_tested_with_the_key_and_its_models_are_catalogued()
    {
        using var server = StartServer();
        var key = $"test-key-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";
        var (client, connection, spaceId) = await CreateLocalConnection(server, key);
        var path = $"/api/spaces/{spaceId}/model-connections/{connection.Id}";

        var test = (await (await client.Post($"{path}/test")).Content.ReadFromJsonAsync<ConnectionTestResponse>(Token))!;
        var refresh = (await (await client.Post($"{path}/models/refresh")).Content.ReadFromJsonAsync<ModelRefreshResponse>(Token))!;

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
        var (client, connection, spaceId) = await CreateLocalConnection(server, null);
        await SetSpacePrivateAddresses(spaceId, allowed: false);

        var test = (await (await client.Post($"/api/spaces/{spaceId}/model-connections/{connection.Id}/test")).Content
            .ReadFromJsonAsync<ConnectionTestResponse>(Token))!;

        test.Succeeded.Should().BeFalse();
        test.Failure.Should().Be("unreachable");
        test.Message.Should().Contain("private addresses");
        server.Requests.Should().Be(0);
    }

    #endregion
}

public sealed class HostedModelConnectionOutboundTests(PostgresServer postgres) : ModelConnectionOutboundTests(postgres)
{
    #region Properties

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
    public async Task The_hosted_service_refuses_to_send_a_model_request_to_a_private_address()
    {
        using var server = StartServer();
        var (client, connection, spaceId) = await CreateLocalConnection(server, null);
        var path = $"/api/spaces/{spaceId}/model-connections/{connection.Id}";

        var test = (await (await client.Post($"{path}/test")).Content.ReadFromJsonAsync<ConnectionTestResponse>(Token))!;
        var refresh = (await (await client.Post($"{path}/models/refresh")).Content.ReadFromJsonAsync<ModelRefreshResponse>(Token))!;

        test.Succeeded.Should().BeFalse();
        test.Message.Should().Contain("private addresses");
        test.Connection.Status.Should().Be("failed");
        refresh.Succeeded.Should().BeFalse();
        refresh.Message.Should().Contain("private addresses");
        server.Requests.Should().Be(0);
    }

    #endregion
}
