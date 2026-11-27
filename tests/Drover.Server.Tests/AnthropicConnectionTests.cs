namespace Drover.Server.Tests;

/// <summary>
/// An Anthropic connection over the real server, with the provider answered by a handler in place of the network: a
/// key and nothing else makes it, a test says it is connected, and its models come into the catalog with what the
/// provider says each can do. The key is never in a response or a log line.
/// </summary>
public sealed class AnthropicConnectionTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string ModelList =
        """
        {"data":[{"type":"model","id":"claude-test-model","display_name":"Claude Test Model","max_input_tokens":1000000,"max_tokens":128000,
        "capabilities":{"image_input":{"supported":true},"pdf_input":{"supported":true},"structured_outputs":{"supported":true},
        "effort":{"supported":true,"low":{"supported":true},"high":{"supported":true}},
        "thinking":{"supported":true,"types":{"adaptive":{"supported":true}}}}}],"has_more":false,"last_id":"claude-test-model"}
        """;

    private readonly List<HttpRequestMessage> sent = [];

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_key_alone_makes_a_connection_that_tests_as_connected_and_lists_its_models()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        var key = $"test-key-{Convert.ToHexString(RandomNumberGenerator.GetBytes(12))}";

        var created = await client.Post(
            $"/api/spaces/{alice.SpaceId}/model-connections",
            new CreateModelConnectionRequest(AnthropicProvider.ConnectionKind, "Anthropic", null, key, null));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Token));
        var connection = (await created.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
        var tested = await client.Post($"/api/spaces/{alice.SpaceId}/model-connections/{connection.Id}/test");
        var test = (await tested.Content.ReadFromJsonAsync<ConnectionTestResponse>(Token))!;
        var models = await client.Get($"/api/spaces/{alice.SpaceId}/model-connections/{connection.Id}/models");
        var catalog = (await models.Content.ReadFromJsonAsync<List<ModelCatalogResponse>>(Token))!;

        test.Succeeded.Should().BeTrue(test.Message);
        test.Message.Should().Be("Connected to Anthropic.");
        test.Connection.Status.Should().Be("ready");
        var model = catalog.Should().ContainSingle().Which;
        model.Model.Should().Be("claude-test-model");
        model.Capabilities.Should().BeEquivalentTo(new ModelCapabilitiesResponse(
            1_000_000, 128_000, true, true, true, true, ["low", "high"], true, false, PdfInput: true));
        model.Price.InputPerMillion.Should().BeNull("the provider's list carries no prices");
        lock (sent)
        {
            sent.Should().OnlyContain(request => request.RequestUri!.Host == "api.anthropic.com"
                && request.Headers.Authorization!.Parameter == key);
        }

        (await tested.Content.ReadAsStringAsync(Token)).Should().NotContain(key);
        (await models.Content.ReadAsStringAsync(Token)).Should().NotContain(key);
        Logs.AllText().Should().NotContain(key);
    }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient(AnthropicProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new Answering(request =>
        {
            lock (sent)
            {
                sent.Add(request);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ModelList, Encoding.UTF8, "application/json") };
        }));

    #endregion

    #region Handler

    private sealed class Answering(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        #region Interface Implementations

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));

        #endregion
    }

    #endregion
}
