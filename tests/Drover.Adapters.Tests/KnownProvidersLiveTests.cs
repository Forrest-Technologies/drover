namespace Drover.Adapters.Tests;

/// <summary>
/// Lists models on each named provider whose key is in the environment, as the variable <c>DROVER_TEST_KEY_{ID}</c>
/// (the id upper-cased, a hyphen written as an underscore, for example <c>DROVER_TEST_KEY_ZAI_CODING</c>). A row with no
/// key in the environment is skipped, and a local row needs a server answering on its own address. These tests are
/// explicit and carry the Live trait: the normal run and pull request CI never execute them. Run them with:
/// <c>DROVER_TEST_KEY_GROQ=... dotnet test --project tests/Drover.Adapters.Tests -- --explicit only --filter-trait "Live=true"</c>.
/// </summary>
[Trait("Live", "true")]
public sealed class KnownProvidersLiveTests
{
    #region Properties

    public static TheoryData<string> Ids => [.. KnownProviders.All.Select(row => row.Id)];

    #endregion

    #region Public Methods

    [Theory(Explicit = true)]
    [MemberData(nameof(Ids))]
    public async Task Lists_models_on_a_row_whose_key_is_present(string id)
    {
        var row = KnownProviders.All.Single(candidate => candidate.Id == id);
        var key = Environment.GetEnvironmentVariable($"DROVER_TEST_KEY_{id.ToUpperInvariant().Replace('-', '_')}");

        if (string.IsNullOrEmpty(key) && row.KeyRule == ModelProviderKeyRule.Required)
        {
            Assert.Skip($"DROVER_TEST_KEY_{id.ToUpperInvariant().Replace('-', '_')} is not set.");
        }

        var services = new ServiceCollection();
        services.AddOpenAICompatibleProvider();
        var clients = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var provider = new OpenAICompatibleProvider(clients);

        var result = await provider.TestConnection(
            new ModelEndpoint(OpenAICompatibleProvider.ConnectionKind, new Uri(row.Address)),
            string.IsNullOrEmpty(key) ? null : key,
            TestContext.Current.CancellationToken);

        result.Succeeded.Should().BeTrue(result.Message);
    }

    #endregion
}
