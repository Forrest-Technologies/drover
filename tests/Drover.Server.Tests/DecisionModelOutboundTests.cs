namespace Drover.Server.Tests;

/// <summary>
/// The decision model clients wired by <see cref="ModelServiceExtensions.AddDroverDecisionModels"/> go through the
/// checked outbound handler: a private address is refused where the deployment or the space forbids one, with the
/// refusal's own words, and is reached only where both allow it. Outside a space's attempt nothing private is reached.
/// Every value here is a sample.
/// </summary>
public sealed class DecisionModelOutboundTests
{
    #region Private Fields

    private const string Answer = """{"model":"local-judge","answers":{"q1":{"type":"noul","noul":0.9}},"usage":{"input_tokens":12,"output_tokens":0}}""";

    #endregion

    #region Public Methods

    [Fact]
    public async Task Every_kind_of_this_build_is_registered()
    {
        await using var provider = Build(allowPrivateAddresses: true);

        provider.GetServices<IDecisionModelProvider>().Select(kind => kind.Kind)
            .Should().Equal("typesafe", "cloudflare_workers_ai", "openrouter", "microsoft_foundry", "systemone_compatible");
    }

    [Fact]
    public async Task A_private_address_is_refused_where_the_deployment_forbids_private_addresses()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok(Answer));
        await using var provider = Build(allowPrivateAddresses: false);

        var asking = () => Ask(provider, server);

        (await asking.Should().ThrowAsync<ModelProviderException>()).Which.Message.Should().Contain("private");
        server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_private_address_is_refused_where_the_space_forbids_private_addresses()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok(Answer));
        await using var provider = Build(allowPrivateAddresses: true);
        using var outbound = OutboundAttempt.Begin(new EffectivePolicy(AllowPrivateAddresses: false, AutoLoadRemoteImages: false));

        var asking = () => Ask(provider, server);

        await asking.Should().ThrowAsync<ModelProviderException>();
        server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_private_address_is_reached_where_both_allow_it()
    {
        using var server = new TestWebServer(_ => TestWebServer.Ok(Answer));
        await using var provider = Build(allowPrivateAddresses: true);
        using var outbound = OutboundAttempt.Begin(new EffectivePolicy(AllowPrivateAddresses: true, AutoLoadRemoteImages: false));

        var answer = await Ask(provider, server);

        answer.Answers["sends_mail"].Yes.Should().Be(0.9);
        server.Requests.Should().Be(1);
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<DecisionAnswer> Ask(ServiceProvider provider, TestWebServer server)
    {
        var compatible = provider.GetServices<IDecisionModelProvider>().Single(kind => kind.Kind == "systemone_compatible");
        var client = compatible.CreateClient(new ModelEndpoint("systemone_compatible", new Uri($"http://127.0.0.1:{server.Port}/api/systemone")), null, "local-judge");
        return client.Decide(
            new DecisionRequest(new Dictionary<string, string> { ["action"] = "Send an email." }, [new DecisionQuestion("sends_mail", "Is it an email?")]),
            Token);
    }

    private static ServiceProvider Build(bool allowPrivateAddresses)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Drover:Policy:Rules:{nameof(DeploymentPolicy.AllowPrivateAddresses)}"] = allowPrivateAddresses ? "true" : "false",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDroverPolicy(configuration);
        services.AddDroverDecisionModels();
        return services.BuildServiceProvider();
    }

    #endregion
}
