namespace Drover.Adapters.Tests;

/// <summary>
/// Runs the Copilot adapter against GitHub with a GitHub user token the owner supplies in
/// DROVER_TEST_COPILOT_GITHUB_TOKEN (a token from a completed registered-app sign-in). These tests are explicit
/// and carry the Live trait: the normal run, and pull request CI, never execute them. Run them with:
/// <c>DROVER_TEST_COPILOT_GITHUB_TOKEN=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-trait "Live=true" --filter-class Drover.Adapters.Tests.CopilotLiveTests</c>.
/// Each run bills the signed-in Copilot plan.
/// </summary>
[Trait("Live", "true")]
public sealed class CopilotLiveTests
{
    #region Constants

    private const string TokenVariable = "DROVER_TEST_COPILOT_GITHUB_TOKEN";

    #endregion

    #region Direct bearer (#526)

    /// <summary>
    /// The GitHub user token is the chat bearer. If this cannot list models or complete a reply, ADR 0029 is
    /// reopened rather than impersonating another client.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task Registered_app_token_lists_models_and_answers()
    {
        var live = Build();
        var token = ReadToken();
        var connection = new ModelEndpoint(ModelConnectionKinds.CopilotSignIn, live.Provider.FixedAddress!);

        var models = await live.Provider.ListModels(connection, token, TestContext.Current.CancellationToken);
        models.Should().NotBeEmpty("a registered-app GitHub token should list the signed-in plan's models");
        Note($"Registered-app token listed {models.Count} models. First: {models[0].Id}.");

        var client = live.Provider.CreateChatClient(connection, token, models[0].Id, new ModelSettings());
        var response = await client.Ask("Reply with the single word: ready", new ChatOptions { MaxOutputTokens = 32 });
        response.Text.Should().NotBeNullOrWhiteSpace();
        Note($"Registered-app chat from {models[0].Id}: {response.Text}");
    }

    #endregion

    #region Helpers

    private static Live Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCopilotPlanProvider();
        var provider = services.BuildServiceProvider();
        return new(provider.GetRequiredService<CopilotPlanProvider>());
    }

    private static string ReadToken() =>
        Environment.GetEnvironmentVariable(TokenVariable)
        ?? throw new InvalidOperationException(
            $"Set {TokenVariable} to a GitHub user token from a completed Copilot registered-app sign-in to run the live tests.");

    private static void Note(string message) => TestContext.Current.SendDiagnosticMessage(message);

    #endregion

    #region Nested Types

    private sealed record Live(CopilotPlanProvider Provider);

    #endregion
}
