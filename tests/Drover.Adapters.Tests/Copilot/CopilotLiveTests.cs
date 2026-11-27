namespace Drover.Adapters.Tests;

/// <summary>
/// Runs the Copilot adapter against GitHub with a GitHub token the owner supplies in
/// DROVER_TEST_COPILOT_GITHUB_TOKEN (a token from a completed device-code sign-in). These tests are
/// explicit and carry the Live trait: the normal run, and pull request CI, never execute them. Run them with:
/// <c>DROVER_TEST_COPILOT_GITHUB_TOKEN=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-trait "Live=true" --filter-class Drover.Adapters.Tests.CopilotLiveTests</c>.
/// Each run bills the signed-in Copilot plan.
/// </summary>
[Trait("Live", "true")]
public sealed class CopilotLiveTests
{
    #region Constants

    private const string TokenVariable = "DROVER_TEST_COPILOT_GITHUB_TOKEN";

    private const string DirectApiVersion = "2026-08-01";

    #endregion

    #region Exchange flow (#526)

    [Fact(Explicit = true)]
    public async Task Exchange_flow_lists_models_and_answers()
    {
        var live = Build();
        var session = await live.SignIn.Refresh(GitHubTokens(), TestContext.Current.CancellationToken);
        var connection = new ModelEndpoint(ModelConnectionKinds.CopilotSignIn, live.Provider.FixedAddress!);

        var models = await live.Provider.ListModels(connection, session.AccessToken, TestContext.Current.CancellationToken);
        models.Should().NotBeEmpty("the exchange flow should list the signed-in plan's models");
        Note($"Exchange flow listed {models.Count} models. First: {models[0].Id}.");

        var client = live.Provider.CreateChatClient(connection, session.AccessToken, models[0].Id, new ModelSettings());
        var response = await client.Ask("Reply with the single word: ready", new ChatOptions { MaxOutputTokens = 32 });
        response.Text.Should().NotBeNullOrWhiteSpace();
        Note($"Exchange flow chat from {models[0].Id}: {response.Text}");
    }

    #endregion

    #region Direct bearer probe (#1028)

    /// <summary>
    /// Notes whether sending the GitHub token as the chat bearer, with the user endpoint and API version from
    /// #1028, also lists models. The adapter ships the exchange flow; this is for the owner to compare.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Notes_whether_the_direct_bearer_flow_also_lists_models()
    {
        var live = Build(options => options.ApiVersion = DirectApiVersion);
        var token = ReadToken();

        using var userRequest = new HttpRequestMessage(HttpMethod.Get, new Uri("https://api.github.com/copilot_internal/user"));
        userRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        using var user = await live.Http.CreateClient(CopilotPlanProvider.AuthHttpClientName)
            .SendAsync(userRequest, TestContext.Current.CancellationToken);
        Note($"Direct user endpoint HTTP {(int)user.StatusCode}.");

        using var modelsRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(live.Options.ApiAddress, "models"));
        modelsRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        using var models = await live.Http.CreateClient(CopilotPlanProvider.HttpClientName)
            .SendAsync(modelsRequest, TestContext.Current.CancellationToken);
        Note($"Direct models list HTTP {(int)models.StatusCode}.");
        if (models.IsSuccessStatusCode)
        {
            using var document = JsonDocument.Parse(await models.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            var count = document.RootElement.TryGetProperty("data", out var data) && data.ValueKind is JsonValueKind.Array
                ? data.GetArrayLength()
                : 0;
            Note($"Direct bearer listed {count} models.");
        }
    }

    #endregion

    #region Helpers

    private static Live Build(Action<CopilotPlanOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCopilotPlanProvider();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        var provider = services.BuildServiceProvider();
        return new(
            provider.GetRequiredService<CopilotPlanProvider>(),
            (CopilotPlanSignIn)provider.GetRequiredService<IModelSignIn>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IOptions<CopilotPlanOptions>>().Value);
    }

    private static ModelTokens GitHubTokens() =>
        new()
        {
            AccessToken = "unused",
            RefreshToken = ReadToken(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };

    private static string ReadToken() =>
        Environment.GetEnvironmentVariable(TokenVariable)
        ?? throw new InvalidOperationException($"Set {TokenVariable} to a GitHub token from a completed Copilot device-code sign-in to run the live tests.");

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion

    #region Nested Types

    private sealed record Live(
        CopilotPlanProvider Provider,
        CopilotPlanSignIn SignIn,
        IHttpClientFactory Http,
        CopilotPlanOptions Options);

    #endregion
}
