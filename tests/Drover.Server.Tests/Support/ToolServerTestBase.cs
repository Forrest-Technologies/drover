namespace Drover.Server.Tests.Support;

/// <summary>
/// For tests that put a real tool server on loopback behind the real adapter, worker, and API, with the scripted
/// model in place of a provider.
/// </summary>
public abstract class ToolServerTestBase(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private static readonly TimeSpan patience = TimeSpan.FromSeconds(30);

    #endregion

    #region Properties

    /// <summary>What the model will say. Set it before the first request reaches the server.</summary>
    protected ScriptedModel Script { get; set; } = new();

    protected ScriptedModelProvider? Model { get; private set; }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => Model ??= new ScriptedModelProvider(Script));

    #endregion

    #region Helpers

    protected async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>The tool server of these tests listens on loopback, which a space must allow.</summary>
    protected async Task AllowPrivateAddresses(Guid spaceId, bool allowed = true)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, Guid.CreateVersion7());
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var space = await scope.ServiceProvider.GetRequiredService<DroverDbContext>().Spaces.SingleAsync(row => row.Id == spaceId, Token);
        space.Settings = new SpaceSettings { AllowPrivateAddresses = allowed };
        await work.Commit(Token);
    }

    /// <summary>Grants the bot one of the connection's tools, as a person would in the bot editor.</summary>
    protected async Task Grant(Guid spaceId, Guid userId, Guid botId, string toolName)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var tool = await db.Tools.SingleAsync(found => found.Name == toolName, Token);
        db.ToolGrants.Add(new ToolGrant { BotId = botId, ToolId = tool.Id, Effect = GrantEffect.Allow, GrantedByUserId = userId });
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    /// <summary>
    /// Leaves the catalog as if the tool had been listed, and reviewed, with other words, so the server's real
    /// definition is a change the next time the connection is listed. The test server's definitions are fixed.
    /// </summary>
    protected async Task PretendTheToolWasReviewedAs(Guid spaceId, Guid toolId, string description)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, Guid.CreateVersion7());
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        var tool = await db.Tools.SingleAsync(found => found.Id == toolId, Token);
        tool.Description = description;
        tool.DefinitionHash = "earlier";
        await db.SaveChangesAsync(Token);
        await work.Commit(Token);
    }

    protected static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId)
    {
        var connection = await PostFor<ModelConnectionSummary>(
            client,
            $"/api/spaces/{spaceId}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));
        return await PostFor<BotSummary>(client, $"/api/spaces/{spaceId}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null));
    }

    protected static async Task<T> PostFor<T>(ApiClient client, string path, object body)
    {
        var response = await client.Post(path, body);
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    protected static async Task<RunSummary> WaitForRun(ApiClient client, Guid spaceId, Guid runId)
    {
        var watch = Stopwatch.StartNew();

        while (true)
        {
            var run = (await (await client.Get($"/api/spaces/{spaceId}/runs/{runId}")).Content.ReadFromJsonAsync<RunSummary>(Token))!;

            if (run.Status is "completed" or "failed" or "cancelled" or "waiting")
            {
                return run;
            }

            (watch.Elapsed < patience).Should().BeTrue($"run {runId} should finish; it is {run.Status}");
            await Task.Delay(25, Token);
        }
    }

    #endregion
}
