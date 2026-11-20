namespace Drover.Server.Tests;

/// <summary>The registered server worker clears abandoned uploads without a cleanup request from a client.</summary>
public sealed class UploadSweeperTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedModelProvider? model;

    #endregion

    #region Public Methods

    [Fact]
    public async Task Server_startup_removes_an_abandoned_upload_without_a_cleanup_request()
    {
        var person = await SeedPerson("alice");
        await Services.GetServices<IHostedService>().OfType<UploadSweeper>().Single().StopAsync(Token);
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var space = $"/api/spaces/{person.SpaceId}";
        var connection = await Read<ModelConnectionSummary>(await client.Post(
            $"{space}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")));
        var bot = await Read<BotSummary>(await client.Post(
            $"{space}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)));
        var thread = await Read<ThreadSummary>(await client.Put($"{space}/bots/{bot.Id}/main-thread"));
        var file = await Read<FileResponse>(await client.PostContent(
            $"{space}/threads/{thread.Id}/files?name=sample.txt", new ByteArrayContent("sample"u8.ToArray())));

        await using (var scope = Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            provider.GetRequiredService<TenantContext>().Set(person.SpaceId, person.UserId);
            await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
            var db = provider.GetRequiredService<DroverDbContext>();
            await db.Files.Where(row => row.Id == file.Id).ExecuteUpdateAsync(
                set => set.SetProperty(row => row.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2)), Token);
            await work.Commit(Token);
        }

        (await State(person.SpaceId, person.UserId, file.Id)).Should().Be((true, false, file.Size));
        await using (var stored = await Services.GetRequiredService<IFileStore>().Open(
            person.SpaceId, FileStorageKeys.For(person.SpaceId, file.Id), Token))
        {
            stored.Should().NotBeNull();
        }

        await RestartServer();

        Services.GetServices<IHostedService>().OfType<UploadSweeper>().Should().ContainSingle();
        var elapsed = Stopwatch.StartNew();
        while (await State(person.SpaceId, person.UserId, file.Id) != (false, false, 0L))
        {
            elapsed.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), "the registered worker runs at startup");
            await Task.Delay(20, Token);
        }

        await using var missing = await Services.GetRequiredService<IFileStore>().Open(
            person.SpaceId, FileStorageKeys.For(person.SpaceId, file.Id), Token);
        missing.Should().BeNull("the worker removes the bytes from the local store before forgetting the retry record");
    }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Private Methods

    private async Task<(bool FileExists, bool Pending, long Bytes)> State(Guid spaceId, Guid userId, Guid fileId)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var db = provider.GetRequiredService<DroverDbContext>();
        var exists = await db.Files.AnyAsync(file => file.Id == fileId, Token);
        var pending = await db.PendingUploadDeletions.AnyAsync(file => file.Id == fileId, Token);
        var bytes = await db.Spaces.Where(space => space.Id == spaceId)
            .Select(space => space.StoredFileBytes).SingleAsync(Token);
        await work.Commit(Token);
        return (exists, pending, bytes);
    }

    private static async Task<T> Read<T>(HttpResponseMessage response) where T : class
    {
        using (response)
        {
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>(Token)
                ?? throw new InvalidOperationException("The test server returned no response body.");
        }
    }

    #endregion
}
