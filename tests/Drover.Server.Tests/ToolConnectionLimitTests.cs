namespace Drover.Server.Tests;

/// <summary>
/// The deployment policy's limits on a tool server, applied through the API with the real adapter: a server chooses
/// how much it sends, and the operator how much this server keeps.
/// </summary>
public sealed class ToolConnectionLimitTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Overrides

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        ["Drover:Policy:Rules:MaxToolsPerConnection"] = "2",
        ["Drover:Policy:Rules:MaxToolResponseBytes"] = "100000",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Tests

    [Fact]
    public async Task A_connection_keeps_no_more_tools_than_the_deployment_policy_allows()
    {
        await using var server = await SdkToolServer.Start(new ToolServerSpec(ServerGeneration.Stateless));
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        await using (var scope = Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(alice.SpaceId, alice.UserId);
            await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
            (await scope.ServiceProvider.GetRequiredService<DroverDbContext>().Spaces.SingleAsync(space => space.Id == alice.SpaceId, Token))
                .Settings = new SpaceSettings { AllowPrivateAddresses = true };
            await work.Commit(Token);
        }

        var created = await client.Post($"/api/spaces/{alice.SpaceId}/tool-connections", new CreateToolConnectionRequest("Many", server.Url.ToString()));
        var listed = (await created.Content.ReadFromJsonAsync<ToolConnectionRefresh>(Token))!;

        listed.Problem.Should().BeNull();
        listed.Tools.Should().HaveCount(2, "the server lists five tools and the policy allows two");
    }

    #endregion
}
