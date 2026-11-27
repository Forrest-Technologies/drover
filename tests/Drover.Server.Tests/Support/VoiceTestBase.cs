namespace Drover.Server.Tests.Support;

/// <summary>A server whose speech providers are the stand-in, and the steps voice tests share.</summary>
public abstract class VoiceTestBase(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Properties

    protected static TimeSpan Patience { get; } = TimeSpan.FromSeconds(30);

    protected static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

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
        JsonElement.Parse(await response.Content.ReadAsStringAsync(Token)).GetProperty("title").GetString()!;

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

    #region A Call's Socket

    protected async Task<WebSocket> Call(ApiClient client, Guid spaceId, Guid threadId, string? origin = null)
    {
        var sockets = NewSocketClient();
        sockets.ConfigureRequest = request =>
        {
            request.Headers.Cookie = client.CookieHeader;

            if (origin is not null)
            {
                request.Headers.Origin = origin;
            }
        };
        return await sockets.ConnectAsync(new Uri($"wss://localhost{Space(spaceId)}/threads/{threadId}/call"), Token);
    }

    protected static Task Send(WebSocket socket, VoiceCallFrame frame) =>
        socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(frame, Json), WebSocketMessageType.Text, true, Token);

    protected static async Task<(WebSocketMessageType Type, byte[] Bytes)> Next(WebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        var received = await socket.ReceiveAsync(buffer, Token).WaitAsync(Patience, Token);
        return (received.MessageType, buffer[..received.Count]);
    }

    protected static async Task<VoiceCallFrame> NextFrame(WebSocket socket)
    {
        var (type, bytes) = await Next(socket);
        type.Should().Be(WebSocketMessageType.Text, "a text frame was expected");
        return JsonSerializer.Deserialize<VoiceCallFrame>(bytes, Json)!;
    }

    protected static async Task<byte[]> NextAudio(WebSocket socket)
    {
        var (type, bytes) = await Next(socket);
        type.Should().Be(WebSocketMessageType.Binary);
        return bytes;
    }

    protected static async Task<List<VoiceCallFrame>> FramesUntilEnded(WebSocket socket)
    {
        List<VoiceCallFrame> frames = [];

        while (frames.Count == 0 || frames[^1].Type != VoiceCallMessages.Ended)
        {
            var (type, bytes) = await Next(socket);
            type.Should().NotBe(WebSocketMessageType.Close, "the call says why it ended before it closes");

            if (type is WebSocketMessageType.Text)
            {
                frames.Add(JsonSerializer.Deserialize<VoiceCallFrame>(bytes, Json)!);
            }
        }

        return frames;
    }

    protected static async Task WaitFor(Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();

        while (!condition())
        {
            (waited.Elapsed < Patience).Should().BeTrue("the condition should come true in time");
            await Task.Delay(20, Token);
        }
    }

    /// <summary>A run that is queued on the conversation, as a message sent from another device would leave one.</summary>

    /// <summary>A run of the bot in the conversation that waits on a person, as one started from another screen would.</summary>
    protected Task SeedActiveRun(Guid spaceId, Guid userId, Guid threadId, Guid botId) =>
        InSpace(spaceId, userId, services =>
        {
            var id = Guid.CreateVersion7();
            services.GetRequiredService<DroverDbContext>().Runs.Add(new Run
            {
                Id = id,
                RootRunId = id,
                ThreadId = threadId,
                BotId = botId,
                PrincipalUserId = userId,
                Status = RunStatus.Waiting,
                WaitingSince = Time.GetUtcNow(),
            });
            return Task.CompletedTask;
        });

    #endregion
}
