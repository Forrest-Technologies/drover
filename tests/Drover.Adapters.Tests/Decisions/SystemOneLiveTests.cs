namespace Drover.Adapters.Tests.Decisions;

/// <summary>
/// Runs each decision model kind against its real provider with a key, a model and, where the kind needs one, an
/// address the owner supplies: DROVER_TEST_DECISION_{KIND}_KEY, _MODEL and _ADDRESS, where KIND is the kind in capitals
/// (TYPESAFE, CLOUDFLARE_WORKERS_AI, OPENROUTER, MICROSOFT_FOUNDRY, SYSTEMONE_COMPATIBLE). For Cloudflare the address is
/// the account's, https://api.cloudflare.com/client/v4/accounts/{account}/. No key, model name or address is written in
/// code. These tests are explicit and carry the Live trait: the normal run, and pull request CI, never execute them.
/// Run one with:
/// <c>DROVER_TEST_DECISION_TYPESAFE_KEY=... DROVER_TEST_DECISION_TYPESAFE_MODEL=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-class "*SystemOneLiveTests"</c>.
/// Each makes one or two requests of a few hundred tokens.
/// </summary>
[Trait("Live", "true")]
public sealed class SystemOneLiveTests
{
    #region Tests

    [Fact(Explicit = true)]
    public Task TypeSafe_answers_the_test_and_a_check() => AnswersTheTestAndACheck(SystemOneKinds.TypeSafe);

    [Fact(Explicit = true)]
    public Task Cloudflare_answers_the_test_and_a_check() => AnswersTheTestAndACheck(SystemOneKinds.CloudflareWorkersAi);

    [Fact(Explicit = true)]
    public Task OpenRouter_answers_the_test_and_a_check() => AnswersTheTestAndACheck(SystemOneKinds.OpenRouter);

    [Fact(Explicit = true)]
    public Task Foundry_answers_the_test_and_a_check() => AnswersTheTestAndACheck(SystemOneKinds.MicrosoftFoundry);

    [Fact(Explicit = true)]
    public Task A_compatible_server_answers_the_test_and_a_check() => AnswersTheTestAndACheck(SystemOneKinds.Compatible);

    #endregion

    #region Private Methods

    private static async Task AnswersTheTestAndACheck(SystemOneKind kind)
    {
        var services = new ServiceCollection();
        services.AddSystemOneDecisionModels();
        var provider = services.BuildServiceProvider().GetServices<IDecisionModelProvider>().Single(candidate => candidate.Kind == kind.Kind);
        var endpoint = new ModelEndpoint(kind.Kind, kind.FixedAddress ?? new Uri(Read(kind, "ADDRESS")));
        var key = kind.KeyRequired ? Read(kind, "KEY") : Environment.GetEnvironmentVariable(Variable(kind, "KEY"));
        var model = Read(kind, "MODEL");

        var tested = await provider.TestConnection(endpoint, key, model, TestContext.Current.CancellationToken);
        var answer = await provider.CreateClient(endpoint, key, model).Decide(
            new DecisionRequest(
                new Dictionary<string, string> { ["action"] = """{"tool":"email.send","kind":"write","arguments":{"to":"sam@example.org"}}""" },
                [
                    new DecisionQuestion("applies", "The rule is: \"Always ask me before sending an email.\" Is the action something this rule is about?"),
                    new DecisionQuestion("risk", "How risky is the action?") { Choices = ["low", "high"] },
                ]),
            TestContext.Current.CancellationToken);

        tested.Succeeded.Should().BeTrue(tested.Message);
        answer.Answers["applies"].Yes.Should().BeGreaterThan(0.5);
        answer.Answers["risk"].Choices.Keys.Should().BeEquivalentTo(["low", "high"]);
        TestContext.Current.SendDiagnosticMessage($"{kind.Kind}: {tested.Message} applies {answer.Answers["applies"].Yes}; {answer.InputTokens} input tokens");
    }

    private static string Variable(SystemOneKind kind, string part) => $"DROVER_TEST_DECISION_{kind.Kind.ToUpperInvariant()}_{part}";

    private static string Read(SystemOneKind kind, string part) =>
        Environment.GetEnvironmentVariable(Variable(kind, part))
        ?? throw new InvalidOperationException($"Set {Variable(kind, part)} to run the live test of {kind.DisplayName}.");

    #endregion
}
