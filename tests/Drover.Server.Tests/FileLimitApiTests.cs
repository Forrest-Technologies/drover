namespace Drover.Server.Tests;

/// <summary>Each file limit of the deployment policy, over HTTP: refused with a sentence that names the limit.</summary>
public sealed class FileLimitApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedModelProvider? model;

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [$"Drover:Policy:Rules:{nameof(DeploymentPolicy.MaxFileBytes)}"] = "2048",
        [$"Drover:Policy:Rules:{nameof(DeploymentPolicy.MaxFilesPerMessage)}"] = "1",
        [$"Drover:Policy:Rules:{nameof(DeploymentPolicy.MaxSpaceFileMegabytes)}"] = "1",
    };

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Tests

    [Fact]
    public async Task A_file_past_MaxFileBytes_is_refused_before_its_body_is_read_with_a_sentence_naming_the_limit()
    {
        var (client, spaceId, threadId) = await Conversation();

        var refused = await client.PostContent(UploadPath(spaceId, threadId, "big.txt"), Text(5000));

        refused.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        (await Title(refused)).Should().Be("This file is 4.9 KB. This server takes files up to 2 KB.");
    }

    [Fact]
    public async Task A_message_with_more_files_than_MaxFilesPerMessage_is_refused_with_a_sentence_naming_the_limit()
    {
        var (client, spaceId, threadId) = await Conversation();
        var first = await Upload(client, spaceId, threadId, "a.txt");
        var second = await Upload(client, spaceId, threadId, "b.txt");

        var refused = await client.Post($"{Space(spaceId)}/threads/{threadId}/messages", new SendMessageRequest("two", FileIds: [first, second]));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(refused)).Should().Be("A message carries at most 1 files on this server.");
    }

    [Fact]
    public async Task A_file_that_would_take_the_space_past_MaxSpaceFileMegabytes_is_refused_with_a_sentence_naming_the_limit()
    {
        var (client, spaceId, threadId) = await Conversation();

        for (var count = 0; count < 512; count++)
        {
            await Upload(client, spaceId, threadId, $"{count}.txt");
        }

        var refused = await client.PostContent(UploadPath(spaceId, threadId, "one-more.txt"), Text(2048));

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Token);
        problem.GetProperty("title").GetString().Should().Be("This space keeps at most 1 MB of files, and this one would take it past that.");
        problem.GetProperty("refusal").GetString().Should().Be(FileRefusals.SpaceFull);
    }

    #endregion

    #region Private Methods

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string UploadPath(Guid spaceId, Guid threadId, string name) =>
        $"{Space(spaceId)}/threads/{threadId}/files?name={Uri.EscapeDataString(name)}";

    private static ByteArrayContent Text(int length) => new(Encoding.UTF8.GetBytes(new string('t', length)));

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private static async Task<Guid> Upload(ApiClient client, Guid spaceId, Guid threadId, string name)
    {
        var response = await client.PostContent(UploadPath(spaceId, threadId, name), Text(2048));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<FileResponse>(Token))!.Id;
    }

    private async Task<(ApiClient Client, Guid SpaceId, Guid ThreadId)> Conversation()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var connection = await client.Post(
            $"{Space(person.SpaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"));
        var connectionId = (await connection.Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!.Id;
        var bot = await client.Post($"{Space(person.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connectionId, null));
        var botId = (await bot.Content.ReadFromJsonAsync<BotSummary>(Token))!.Id;
        var thread = await client.Put($"{Space(person.SpaceId)}/bots/{botId}/main-thread");
        return (client, person.SpaceId, (await thread.Content.ReadFromJsonAsync<ThreadSummary>(Token))!.Id);
    }

    #endregion
}
