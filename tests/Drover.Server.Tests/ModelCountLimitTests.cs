namespace Drover.Server.Tests;

/// <summary>The cap on how many models one connection may add to the catalog, set by the deployment policy.</summary>
public sealed class ModelCountLimitTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedProvider Scripted { get; } = new();

    #endregion

    #region Public Methods

    [Fact]
    public async Task A_list_with_more_models_than_the_policy_allows_is_refused_and_the_catalog_is_left_alone()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        var created = await client.Post($"/api/spaces/{alice.SpaceId}/model-connections", new CreateModelConnectionRequest(ScriptedProvider.ConnectionKind, "S", null, null, null));
        var connection = (await created.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
        var path = $"/api/spaces/{alice.SpaceId}/model-connections/{connection.Id}/models/refresh";
        Scripted.OnList = _ => Task.FromResult<IReadOnlyList<ModelInfo>>([Model("a"), Model("b")]);
        (await client.Post(path)).EnsureSuccessStatusCode();
        Scripted.OnList = _ => Task.FromResult<IReadOnlyList<ModelInfo>>([Model("a"), Model("b"), Model("c")]);

        var refused = (await (await client.Post(path)).Content.ReadFromJsonAsync<ModelRefreshResponse>(Token))!;

        refused.Succeeded.Should().BeFalse();
        refused.Failure.Should().Be("rejected");
        refused.Message.Should().Contain("larger than this server accepts");
        refused.Models.Select(model => model.Model).Should().BeEquivalentTo(["a", "b"]);
    }

    #endregion

    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:Policy:Rules:MaxModelsPerConnection"] = "2",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(Scripted);

    #endregion

    #region Private Methods

    private static ModelInfo Model(string id) => new(id, id, new ModelCapabilities(), []);

    #endregion
}
