namespace Drover.Server.Tests;

/// <summary>
/// A person's sandbox controls over the API, against a provider in memory: each route, the picture's type and cache
/// policy, the antiforgery token on every POST, who may use them, and that no answer carries the provider's ids or
/// tokens or names it.
/// </summary>
public sealed class SandboxControlApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly MemorySandboxProvider provider = new();

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<ISandboxProvider>(provider);
        services.AddSandboxTools();
    }

    #endregion

    #region Controls

    [Fact]
    public async Task An_owner_runs_a_command_sees_the_screen_drives_it_and_pauses_resumes_rebuilds_and_turns_it_off()
    {
        var (client, spaceId, botId) = await DesktopBot("owner");
        var sandbox = $"{Space(spaceId)}/bots/{botId}/sandbox";
        List<string> bodies = [];

        var before = await Read<BotSandboxStatus>(await client.Get(sandbox), bodies);
        before.Should().Be(new BotSandboxStatus(Available: true, HasSandbox: true, "none", null, Desktop: true, BotWorking: false));

        var ran = await Read<SandboxCommandResponse>(await client.Post($"{sandbox}/commands", new SandboxCommandRequest("echo hi")), bodies);
        ran.Should().Match<SandboxCommandResponse>(result => result.ExitStatus == 0 && result.Stdout == "echo hi\n");

        var screen = await client.Post($"{sandbox}/screen");
        screen.StatusCode.Should().Be(HttpStatusCode.OK);
        screen.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        screen.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await screen.Content.ReadAsByteArrayAsync(Token)).Should().Equal(MemorySandboxProvider.Picture);

        var click = await client.Post($"{sandbox}/input", new SandboxInputRequest("click", X: 10, Y: 20));
        click.StatusCode.Should().Be(HttpStatusCode.NoContent);
        provider.Commands.Last().Arguments.Should().Equal(DesktopCommands.Click(10, 20, MouseButton.Left, twice: false).Arguments);

        (await Read<BotSandboxStatus>(await client.Post($"{sandbox}/pause"), bodies)).State.Should().Be("paused");
        (await Read<BotSandboxStatus>(await client.Post($"{sandbox}/resume"), bodies)).State.Should().Be("running");
        var first = provider.Ids.Single();
        (await Read<BotSandboxStatus>(await client.Post($"{sandbox}/rebuild"), bodies)).State.Should().Be("running");
        provider.StateOf(first).Should().Be(ProviderSandboxState.Gone);
        provider.Live.Should().Be(1);
        (await Read<BotSandboxStatus>(await client.Post($"{sandbox}/turn-off"), bodies)).State.Should().Be("none");
        provider.Live.Should().Be(0);

        bodies.Should().OnlyContain(body => provider.Ids.All(id => !body.Contains(id, StringComparison.Ordinal))
            && provider.Tokens.All(token => !body.Contains(token, StringComparison.Ordinal))
            && !body.Contains(provider.Kind, StringComparison.OrdinalIgnoreCase));
        Logs.AllText().Should().NotContainAny(provider.Tokens);
        var actions = await AuditActions(spaceId);
        actions.Should().Contain(["sandbox.command", "sandbox.input", "sandbox.paused", "sandbox.resumed", "sandbox.rebuilt", "sandbox.turned_off"]);
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("rebuild")]
    [InlineData("turn-off")]
    [InlineData("screen")]
    [InlineData("input")]
    [InlineData("commands")]
    public async Task Every_post_needs_the_antiforgery_token_and_without_it_nothing_happens(string route)
    {
        var (client, spaceId, botId) = await DesktopBot("forger");
        object? body = route switch
        {
            "input" => new SandboxInputRequest("key", Keys: ["enter"]),
            "commands" => new SandboxCommandRequest("touch /tmp/forged"),
            _ => null,
        };

        var refused = await client.PostWithoutToken($"{Space(spaceId)}/bots/{botId}/sandbox/{route}", body);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        provider.Ids.Should().BeEmpty();
        provider.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_who_may_not_change_the_bot_is_refused_and_a_stranger_finds_nothing()
    {
        var (_, spaceId, botId) = await DesktopBot("keeper");
        var member = await SeedMember(spaceId, "member", MembershipRole.Member);
        var memberClient = await SignIn(member.Email, member.Password);
        var stranger = await SeedPerson("stranger");
        var strangerClient = await SignIn(stranger.Email, stranger.Password);
        var sandbox = $"{Space(spaceId)}/bots/{botId}/sandbox";

        (await memberClient.Get(sandbox)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await memberClient.Post($"{sandbox}/commands", new SandboxCommandRequest("id"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await memberClient.Post($"{sandbox}/screen")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await strangerClient.Get(sandbox)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await strangerClient.Post($"{Space(stranger.SpaceId)}/bots/{botId}/sandbox/commands", new SandboxCommandRequest("id")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "the bot belongs to another space");
        provider.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task A_wrong_input_or_command_is_a_bad_request_in_words_and_a_refusal_is_a_conflict_in_words()
    {
        var (client, spaceId, botId) = await DesktopBot("careful");
        var sandbox = $"{Space(spaceId)}/bots/{botId}/sandbox";

        var unknownKey = await client.Post($"{sandbox}/input", new SandboxInputRequest("key", Keys: ["windows"]));
        var empty = await client.Post($"{sandbox}/commands", new SandboxCommandRequest(" "));
        var noSandboxYet = await client.Post($"{sandbox}/input", new SandboxInputRequest("key", Keys: ["enter"]));

        unknownKey.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(unknownKey)).Should().Contain("no key named");
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        noSandboxYet.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Title(noSandboxYet)).Should().Contain("no sandbox yet");
        provider.Ids.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    /// <summary>A signed-in owner and a bot of theirs with a private sandbox that has a screen.</summary>
    private async Task<(ApiClient Client, Guid SpaceId, Guid BotId)> DesktopBot(string name)
    {
        var person = await SeedPerson(name);
        var client = await SignIn(person.Email, person.Password);
        var connection = await Read<ModelConnectionSummary>(await client.Post(
            $"{Space(person.SpaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted")));
        var bot = await Read<BotSummary>(await client.Post($"{Space(person.SpaceId)}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)));
        var saved = await client.Put(
            $"{Space(person.SpaceId)}/bots/{bot.Id}/definition",
            new UpdateBotRequest(null, null, null, null, null, null, null, null, null, "private", new SandboxSettingsBody("off", [], null, Desktop: true), null, null));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync(Token));
        return (client, person.SpaceId, bot.Id);
    }

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, List<string>? bodies = null)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        response.IsSuccessStatusCode.Should().BeTrue(body);
        bodies?.Add(body);
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }

    private static async Task<string> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString() ?? string.Empty;

    private async Task<List<string>> AuditActions(Guid spaceId)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand("SELECT action, data::text FROM audit_log WHERE space_id = @space ORDER BY id", connection);
        command.Parameters.AddWithValue("space", spaceId);
        await using var reader = await command.ExecuteReaderAsync(Token);
        List<string> actions = [];

        while (await reader.ReadAsync(Token))
        {
            actions.Add(reader.GetString(0));
            reader.GetString(1).Should().NotContain("echo hi", "the audit log never keeps a command");
        }

        return actions;
    }

    #endregion
}


/// <summary>A server without a sandbox provider: the sandbox screen is told so, and every action is refused in words.</summary>
public sealed class NoSandboxControlApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedModelProvider? model;

    #endregion

    #region Controls

    [Fact]
    public async Task The_state_says_no_provider_and_a_command_is_refused_in_words()
    {
        var (email, password, _, spaceId) = await SeedPerson("noprovider");
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        var connection = (await (await client.Post(
            $"/api/spaces/{spaceId}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "Scripted", null, null, "scripted"))).Content.ReadFromJsonAsync<ModelConnectionSummary>(Token))!;
        var bot = (await (await client.Post($"/api/spaces/{spaceId}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)))
            .Content.ReadFromJsonAsync<BotSummary>(Token))!;

        var status = (await (await client.Get($"/api/spaces/{spaceId}/bots/{bot.Id}/sandbox")).Content.ReadFromJsonAsync<BotSandboxStatus>(Token))!;
        var command = await client.Post($"/api/spaces/{spaceId}/bots/{bot.Id}/sandbox/commands", new SandboxCommandRequest("ls"));

        status.Available.Should().BeFalse();
        command.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await command.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString().Should().Contain("No sandbox provider");
    }

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion
}
