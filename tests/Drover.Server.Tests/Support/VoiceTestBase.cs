namespace Drover.Server.Tests.Support;

/// <summary>A server whose speech providers are the stand-in, and the steps voice tests share.</summary>
public abstract class VoiceTestBase(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected FakeSpeech Speech { get; } = new();

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<ISpeechToText>();
        services.RemoveAll<ITextToSpeech>();
        services.RemoveAll<IRealtimeVoice>();
        services.AddSingleton<ISpeechToText>(Speech);
        services.AddSingleton<ITextToSpeech>(Speech);
        services.AddSingleton<IRealtimeVoice>(Speech);
    }

    #endregion

    #region Helpers

    protected static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    protected async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    protected static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.Created], await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    protected static async Task<string> Sentence(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token)).RootElement.GetProperty("title").GetString()!;

    protected static async Task<ModelConnectionSummary> CreateConnection(ApiClient client, Guid spaceId, string name = "Speech", string? key = null) =>
        await Read<ModelConnectionSummary>(await client.Post(
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ModelConnectionKinds.Compatible, name, "https://speech.example.test/v1", key, "plain")));

    /// <summary>Adds catalog rows the way a refresh would, with the speech marks a test needs.</summary>
    protected async Task SeedModels(Guid spaceId, Guid userId, Guid connectionId, params (string Model, ModelCapabilities Capabilities)[] models) =>
        await InSpace(spaceId, userId, services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();

            foreach (var (model, capabilities) in models)
            {
                db.ModelCatalog.Add(new ModelCatalogEntry
                {
                    ModelConnectionId = connectionId,
                    Model = model,
                    DisplayName = model,
                    Capabilities = capabilities,
                    LastSeenAt = Time.GetUtcNow(),
                });
            }

            return Task.CompletedTask;
        });

    protected async Task InSpace(Guid spaceId, Guid userId, Func<IServiceProvider, Task> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        await work(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<DroverDbContext>().SaveChangesAsync(Token);
        await transaction.Commit(Token);
    }

    /// <summary>A bot on a connection whose catalog holds one model for each of listening, speaking and live calls, and its main thread.</summary>
    protected async Task<(ModelConnectionSummary Connection, BotSummary Bot, ThreadSummary Thread)> SeedBotWithSpeech(
        ApiClient client,
        Guid spaceId,
        Guid userId)
    {
        var connection = await CreateConnection(client, spaceId, key: "sk-test-speech-key");
        await SeedModels(
            spaceId,
            userId,
            connection.Id,
            ("plain", new ModelCapabilities()),
            ("whisper-1", new ModelCapabilities { SpeechToText = true }),
            ("tts-1", new ModelCapabilities { TextToSpeech = true }),
            ("realtime-1", new ModelCapabilities { RealtimeVoice = true }));
        var bot = await Read<BotSummary>(await client.Post($"{Space(spaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, "plain")));
        var thread = await Read<ThreadSummary>(await client.Put($"{Space(spaceId)}/bots/{bot.Id}/main-thread"));
        return (connection, bot, thread);
    }

    /// <summary>Puts messages at the end of a conversation, as a run would have.</summary>
    protected Task SeedMessages(Guid spaceId, Guid userId, Guid threadId, params Message[] messages) =>
        InSpace(spaceId, userId, async services =>
        {
            var thread = (await services.GetRequiredService<ThreadRepository>().GetThreadForUpdate(threadId, Token))!;
            await services.GetRequiredService<MessageRepository>().AppendMessages(thread, messages, Token);
        });

    #endregion
}
