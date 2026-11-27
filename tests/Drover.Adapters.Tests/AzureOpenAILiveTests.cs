namespace Drover.Adapters.Tests;

/// <summary>
/// Calls a real Azure OpenAI resource: <c>DROVER_TEST_AZURE_ENDPOINT</c> (the resource's v1 address, for example
/// <c>https://NAME.openai.azure.com/openai/v1/</c>), <c>DROVER_TEST_AZURE_KEY</c>, and optionally
/// <c>DROVER_TEST_AZURE_MODEL</c> (a deployment name). It records which key forms the resource accepts and what its
/// model list holds, as diagnostic messages. Explicit and marked Live: the normal run and pull request CI never
/// execute it. Run it with:
/// <c>dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-trait "Live=true" --filter-class "*AzureOpenAILiveTests"</c>.
/// </summary>
[Trait("Live", "true")]
public sealed class AzureOpenAILiveTests
{
    #region Public Methods

    [Fact(Explicit = true)]
    public async Task Records_which_key_forms_the_resource_accepts_and_what_its_model_list_holds()
    {
        var endpoint = Environment.GetEnvironmentVariable("DROVER_TEST_AZURE_ENDPOINT");
        var key = Environment.GetEnvironmentVariable("DROVER_TEST_AZURE_KEY");

        if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key))
        {
            Assert.Skip("DROVER_TEST_AZURE_ENDPOINT and DROVER_TEST_AZURE_KEY are not both set.");
        }

        var address = new Uri(endpoint);
        var cancellation = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddOpenAICompatibleProvider();
        var clients = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        using var http = clients.CreateClient(OpenAICompatibleProvider.HttpClientName);

        foreach (var form in new[] { "Authorization: Bearer", "api-key" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(address, "models"));

            if (form == "api-key")
            {
                request.Headers.TryAddWithoutValidation("api-key", key);
            }
            else
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
            }

            using var response = await http.SendAsync(request, cancellation);
            TestContext.Current.SendDiagnosticMessage($"GET /models with only {form}: {(int)response.StatusCode}");
        }

        var provider = new OpenAICompatibleProvider(clients);
        var endpointModel = new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, address);
        var models = await provider.ListModels(endpointModel, key, cancellation);
        var result = await provider.TestConnection(endpointModel, key, cancellation);
        TestContext.Current.SendDiagnosticMessage($"The model list holds {models.Count} entries, first ten: {string.Join(", ", models.Take(10).Select(model => model.Id))}");

        result.Succeeded.Should().BeTrue(result.Message);

        if (Environment.GetEnvironmentVariable("DROVER_TEST_AZURE_MODEL") is { Length: > 0 } deployment)
        {
            var reply = await provider.CreateChatClient(endpointModel, key, deployment, new ModelSettings()).Ask("Say hello.");
            reply.Text.Should().NotBeNullOrWhiteSpace();
        }
    }

    #endregion
}
