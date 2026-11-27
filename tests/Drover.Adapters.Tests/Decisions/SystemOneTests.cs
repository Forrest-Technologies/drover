namespace Drover.Adapters.Tests.Decisions;

/// <summary>A decision model kind wired to its fixtures, or to a handler that refuses a private address.</summary>
public static class SystemOneRig
{
    #region Private Fields

    /// <summary>A small bound, so a fixture can be larger than it without being large.</summary>
    public const int TestAnswerBytes = 4096;

    #endregion

    #region Public Methods

    public static (DecisionProviderUnderTest Subject, RecordedHandler? Handler) Start(SystemOneKind kind, string scenario)
    {
        var key = TestKeys.Generate();

        if (scenario == DecisionScenarios.PrivateAddress)
        {
            var refusing = new SystemOneProvider(kind, new FixtureHttpClientFactory(new RefusingHandler()), TestAnswerBytes);
            return (new DecisionProviderUnderTest(refusing, Endpoint(kind), key, Model(kind), () => []) { MaxQuestionsPerRequest = kind.MaxQuestionsPerRequest }, null);
        }

        var handler = new RecordedHandler(RecordedFixture.Load(Path.Combine("systemone", kind.Kind), scenario));
        var provider = new SystemOneProvider(kind, new FixtureHttpClientFactory(handler), TestAnswerBytes * (scenario == DecisionScenarios.Split ? 4 : 1));
        var subject = new DecisionProviderUnderTest(provider, Endpoint(kind), key, Model(kind), handler.Wire) { MaxQuestionsPerRequest = kind.MaxQuestionsPerRequest };
        return (subject, handler);
    }

    /// <summary>The address a connection of the kind has. A fixed-address kind is given one that it must not use.</summary>
    public static ModelEndpoint Endpoint(SystemOneKind kind) => new(
        kind.Kind,
        kind.Kind switch
        {
            "cloudflare_workers_ai" => new Uri("https://api.cloudflare.com/client/v4/accounts/test-account"),
            "microsoft_foundry" => new Uri("https://test-resource.services.ai.azure.com"),
            "systemone_compatible" => new Uri("https://judge.example.test/api/systemone"),
            _ => new Uri("https://wrong.example.test/"),
        });

    public static string Model(SystemOneKind kind) => kind.Kind switch
    {
        "typesafe" => "jev-latest",
        "cloudflare_workers_ai" => "clef-flash",
        "openrouter" => "typesafe/jev-1.14",
        "microsoft_foundry" => "my-decision-deployment",
        _ => "local-judge",
    };

    #endregion

    #region Nested Types

    private sealed class RefusingHandler : HttpMessageHandler
    {
        #region Interface Implementations

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new OutboundBlockedException(DecisionScenarios.PrivateAddressRefusal);

        #endregion
    }

    #endregion
}

public sealed class TypeSafeDecisionConformanceTests : DecisionModelConformance
{
    protected override DecisionProviderUnderTest Start(string scenario) => SystemOneRig.Start(SystemOneKinds.TypeSafe, scenario).Subject;
}

public sealed class CloudflareDecisionConformanceTests : DecisionModelConformance
{
    protected override DecisionProviderUnderTest Start(string scenario) => SystemOneRig.Start(SystemOneKinds.CloudflareWorkersAi, scenario).Subject;
}

public sealed class OpenRouterDecisionConformanceTests : DecisionModelConformance
{
    protected override DecisionProviderUnderTest Start(string scenario) => SystemOneRig.Start(SystemOneKinds.OpenRouter, scenario).Subject;
}

public sealed class FoundryDecisionConformanceTests : DecisionModelConformance
{
    protected override DecisionProviderUnderTest Start(string scenario) => SystemOneRig.Start(SystemOneKinds.MicrosoftFoundry, scenario).Subject;
}

public sealed class CompatibleDecisionConformanceTests : DecisionModelConformance
{
    protected override DecisionProviderUnderTest Start(string scenario) => SystemOneRig.Start(SystemOneKinds.Compatible, scenario).Subject;
}

/// <summary>
/// What is particular to this adapter: each kind's address, header and envelope on the wire, a long list of questions
/// split and joined, and a key that never reaches a message. The fixtures are hand-written from each provider's
/// reference, read 2026-11-26.
/// </summary>
public sealed class SystemOneTests
{
    #region Wire

    public static TheoryData<string, string, string, string?> Documented => new()
    {
        { "typesafe", "https://api.typesafe.ai/v1/systemone", "Authorization", "Bearer " },
        { "cloudflare_workers_ai", "https://api.cloudflare.com/client/v4/accounts/test-account/ai/run/@cf/cloudflare/clef-flash", "Authorization", "Bearer " },
        { "openrouter", "https://openrouter.ai/api/alpha/decisions", "Authorization", "Bearer " },
        { "microsoft_foundry", "https://test-resource.services.ai.azure.com/providers/microsoft/v1/systemone", "api-key", null },
        { "systemone_compatible", "https://judge.example.test/api/systemone", "Authorization", "Bearer " },
    };

    [Theory]
    [MemberData(nameof(Documented))]
    public async Task The_request_is_the_documented_shape_for_each_kind(string kindName, string address, string header, string? prefix)
    {
        var kind = SystemOneKinds.All.Single(candidate => candidate.Kind == kindName);
        var (subject, handler) = SystemOneRig.Start(kind, DecisionScenarios.PickOne);
        var question = new DecisionQuestion("risk", "How risky is the action?") { Choices = ["low", "high"] };

        await subject.CreateClient().Decide(new(new Dictionary<string, string> { ["action"] = "Pay the invoice." }, [question]), CancellationToken.None);

        var sent = handler!.Requests.Should().ContainSingle().Subject;
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Uri.Should().Be(new Uri(address));
        sent.Headers[header].Should().Be($"{prefix}{subject.ApiKey}");
        var body = sent.Json;
        body.GetProperty("model").GetString().Should().Be(subject.Model);
        body.GetProperty("state").GetProperty("action").GetString().Should().Be("Pay the invoice.");
        var asked = body.GetProperty("questions").GetProperty("q1");
        asked.GetProperty("type").GetString().Should().Be("choice");
        asked.GetProperty("instructions").GetString().Should().Be("How risky is the action?");
        asked.GetProperty("criteria").EnumerateObject().Select(choice => choice.Name).Should().Equal("low", "high");
        body.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(["model", "state", "questions"]);
    }

    [Fact]
    public void Each_kind_says_what_a_connection_gives_and_where_its_calls_go()
    {
        var providers = SystemOneKinds.All.ToDictionary(kind => kind.Kind, kind => (IDecisionModelProvider)new SystemOneProvider(kind, new FixtureHttpClientFactory(new RecordedHandler(RecordedFixture.Load(Path.Combine("systemone", kind.Kind), DecisionScenarios.YesOrNo)))));

        providers.Values.Select(provider => (provider.Kind, provider.AddressRule, provider.KeyRule)).Should().Equal(
            ("typesafe", DecisionAddressRule.Fixed, ModelKeyRule.Required),
            ("cloudflare_workers_ai", DecisionAddressRule.Account, ModelKeyRule.Required),
            ("openrouter", DecisionAddressRule.Fixed, ModelKeyRule.Required),
            ("microsoft_foundry", DecisionAddressRule.Address, ModelKeyRule.Required),
            ("systemone_compatible", DecisionAddressRule.Address, ModelKeyRule.Optional));
        providers["typesafe"].AddressFor("https://ignored.example.test/", null).Should().Be(new Uri("https://api.typesafe.ai/"));
        providers["cloudflare_workers_ai"].AddressFor(null, " abc123 ").Should().Be(new Uri("https://api.cloudflare.com/client/v4/accounts/abc123/"));
        providers["cloudflare_workers_ai"].AddressFor(null, null).Should().BeNull();
        providers["microsoft_foundry"].AddressFor("https://res.services.ai.azure.com", null).Should().Be(new Uri("https://res.services.ai.azure.com"));
        providers["systemone_compatible"].AddressFor("ftp://judge.example.test/", null).Should().BeNull("only http and https are addresses");
        providers.Values.Should().OnlyContain(provider => provider.DisplayName.Length > 0);
    }

    [Fact]
    public async Task A_local_server_with_no_key_is_sent_no_key()
    {
        var (subject, handler) = SystemOneRig.Start(SystemOneKinds.Compatible, DecisionScenarios.YesOrNo);

        await subject.Provider.CreateClient(subject.Endpoint, null, subject.Model)
            .Decide(new(new Dictionary<string, string> { ["action"] = "Look." }, [new("look", "Is it a look?")]), CancellationToken.None);

        handler!.Requests.Single().Headers.Should().NotContainKey("Authorization");
    }

    [Fact]
    public async Task A_long_question_list_is_split_and_joined()
    {
        var (subject, handler) = SystemOneRig.Start(SystemOneKinds.TypeSafe, DecisionScenarios.Split);
        DecisionQuestion[] questions = [.. Enumerable.Range(1, 70).Select(index => new DecisionQuestion($"question_{index}", $"Question {index}?"))];

        var answer = await subject.CreateClient().Decide(new(new Dictionary<string, string> { ["action"] = "Act." }, questions), CancellationToken.None);

        handler!.Requests.Should().HaveCount(2);
        handler.Requests[0].Json.GetProperty("questions").EnumerateObject().Should().HaveCount(SystemOneKinds.MaxQuestionsPerRequest);
        handler.Requests[1].Json.GetProperty("questions").EnumerateObject().Select(question => question.Name)
            .Should().Equal([.. Enumerable.Range(65, 6).Select(index => $"q{index}")]);
        handler.Requests.Should().OnlyContain(request => request.Json.GetProperty("state").GetProperty("action").GetString() == "Act.", "every request carries the whole state");
        answer.Answers.Keys.Should().BeEquivalentTo(questions.Select(question => question.Name));
        answer.InputTokens.Should().Be(1000, "the tokens of both requests are added");
    }

    [Fact]
    public async Task A_question_the_provider_did_not_answer_has_no_answer()
    {
        var (subject, _) = SystemOneRig.Start(SystemOneKinds.OpenRouter, DecisionScenarios.YesOrNo);

        var answer = await subject.CreateClient().Decide(
            new(new Dictionary<string, string> { ["action"] = "Act." }, [new("asked", "Asked?"), new("unanswered", "Unanswered?")]),
            CancellationToken.None);

        answer.Answers.Keys.Should().Equal("asked");
    }

    #endregion

    #region Keys

    [Fact]
    public async Task A_key_is_never_logged()
    {
        List<string> said = [];

        foreach (var kind in SystemOneKinds.All)
        {
            foreach (var scenario in new[] { DecisionScenarios.InvalidKey, DecisionScenarios.RateLimited, DecisionScenarios.NotThisApi, DecisionScenarios.TooLarge, DecisionScenarios.PrivateAddress })
            {
                var (subject, _) = SystemOneRig.Start(kind, scenario);

                said.Add(await FailureOf(subject));
                var tested = await subject.Provider.TestConnection(subject.Endpoint, subject.ApiKey, subject.Model, CancellationToken.None);
                said.Add(tested.Message);
                said.Should().OnlyContain(text => !text.Contains(subject.ApiKey!, StringComparison.Ordinal), $"{kind.Kind} {scenario} must not say the key");
            }
        }

        said.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_kind_that_needs_a_key_is_not_tested_without_one()
    {
        var (subject, handler) = SystemOneRig.Start(SystemOneKinds.TypeSafe, DecisionScenarios.TestConnection);

        var result = await subject.Provider.TestConnection(subject.Endpoint, null, subject.Model, CancellationToken.None);

        result.Failure.Should().Be(ModelFailure.InvalidKey);
        handler!.Requests.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    // Everything a failure says about itself, its message and its stack included.
    private static async Task<string> FailureOf(DecisionProviderUnderTest subject)
    {
        try
        {
            await subject.CreateClient().Decide(new(new Dictionary<string, string> { ["action"] = "Act." }, [new("act", "Act?")]), CancellationToken.None);
            return string.Empty;
        }
        catch (ModelProviderException failure)
        {
            return $"{failure.Message}\n{failure}";
        }
    }

    #endregion
}
