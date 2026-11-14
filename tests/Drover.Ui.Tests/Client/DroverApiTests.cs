namespace Drover.Ui.Tests.Client;

public sealed class DroverApiTests : IDisposable
{
    #region Private Fields

    private readonly FakeServer server = new();

    #endregion

    #region Public Methods

    public void Dispose() => server.Dispose();

    [Fact]
    public async Task Nobody_signed_in_is_an_answer_not_a_failure()
    {
        server.Answer("GET", "/api/session", HttpStatusCode.Unauthorized);

        (await server.CreateApi().GetSession(Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task Setup_that_is_closed_is_an_answer_not_a_failure()
    {
        (await server.CreateApi().GetSetupStatus(Xunit.TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task A_refusal_carries_the_servers_own_sentence()
    {
        server.Refuse("POST", "/api/sign-in", HttpStatusCode.Unauthorized, "The email or password is not right.");

        var signIn = () => server.CreateApi().SignIn(new("sam@example.org", "wrong"), Xunit.TestContext.Current.CancellationToken);

        var refusal = (await signIn.Should().ThrowAsync<DroverApiException>()).Which;
        refusal.Message.Should().Be("The email or password is not right.");
        refusal.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "Too many attempts. Wait a minute and try again.")]
    [InlineData(HttpStatusCode.InternalServerError, "The server failed to answer. Try again, and check the server log if it keeps happening.")]
    [InlineData(HttpStatusCode.Forbidden, "Your account is not allowed to do that.")]
    public async Task A_refusal_with_no_sentence_is_shown_with_one_for_its_status(HttpStatusCode status, string sentence)
    {
        server.Answer("POST", "/api/sign-in", status);

        var signIn = () => server.CreateApi().SignIn(new("sam@example.org", "secret"), Xunit.TestContext.Current.CancellationToken);

        var refusal = (await signIn.Should().ThrowAsync<DroverApiException>()).Which;
        refusal.StatusCode.Should().Be(status);
        Failures.Sentence(refusal).Should().Be(sentence);
    }

    [Fact]
    public void A_server_that_cannot_be_reached_is_said_in_words()
    {
        Failures.Sentence(new HttpRequestException("connection refused")).Should().Be(Failures.Unreachable);
    }

    [Fact]
    public async Task A_model_connection_is_created_and_tested_under_its_space()
    {
        var spaceId = Guid.NewGuid();
        var connection = new ModelConnectionSummary(Guid.NewGuid(), "Venice", "venice", null, true, "untested", null, [], null, null);
        server.Answer("POST", $"/api/spaces/{spaceId}/model-connections", connection, HttpStatusCode.Created);
        server.Answer(
            "POST",
            $"/api/spaces/{spaceId}/model-connections/{connection.Id}/test",
            new ConnectionTestResponse(true, "Connected.", null, connection));
        var api = server.CreateApi();

        var created = await api.CreateModelConnection(spaceId, new("venice", "Venice", null, "sample-key", null), Xunit.TestContext.Current.CancellationToken);
        var tested = await api.TestModelConnection(spaceId, created.Id, Xunit.TestContext.Current.CancellationToken);

        created.Id.Should().Be(connection.Id);
        tested.Succeeded.Should().BeTrue();
        tested.Message.Should().Be("Connected.");
    }

    [Fact]
    public async Task The_sandbox_controls_go_to_the_bots_sandbox_and_every_post_carries_the_antiforgery_token()
    {
        var spaceId = Guid.NewGuid();
        var botId = Guid.NewGuid();
        var sandbox = $"/api/spaces/{spaceId}/bots/{botId}/sandbox";
        var running = new BotSandboxStatus(true, true, "running", null, Desktop: true, BotWorking: false);
        var token = Xunit.TestContext.Current.CancellationToken;
        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        server.Answer("GET", sandbox, running);
        server.Answer("POST", $"{sandbox}/pause", running with { State = "paused" });
        server.Answer("POST", $"{sandbox}/resume", running);
        server.Answer("POST", $"{sandbox}/rebuild", running);
        server.Answer("POST", $"{sandbox}/turn-off", running with { State = "none" });
        server.AnswerBytes("POST", $"{sandbox}/screen", png, "image/png");
        server.Answer("POST", $"{sandbox}/input", HttpStatusCode.NoContent);
        server.Answer("POST", $"{sandbox}/commands", new SandboxCommandResponse(0, "ok\n", string.Empty, false, false, false));
        var api = server.CreateApi();
        api.AntiforgeryToken = "sample-antiforgery-token";

        (await api.GetBotSandbox(spaceId, botId, token)).State.Should().Be("running");
        (await api.PauseBotSandbox(spaceId, botId, token)).State.Should().Be("paused");
        (await api.ResumeBotSandbox(spaceId, botId, token)).State.Should().Be("running");
        (await api.RebuildBotSandbox(spaceId, botId, token)).State.Should().Be("running");
        (await api.TurnOffBotSandbox(spaceId, botId, token)).State.Should().Be("none");
        (await api.GetBotSandboxScreen(spaceId, botId, token)).Should().Equal(png);
        await api.SendBotSandboxInput(spaceId, botId, new SandboxInputRequest("key", Keys: ["enter"]), token);
        (await api.RunBotSandboxCommand(spaceId, botId, "echo ok", token)).Stdout.Should().Be("ok\n");

        server.Requests.Select(request => $"{request.Method} {request.Path}").Should().Equal(
            $"GET {sandbox}",
            $"POST {sandbox}/pause",
            $"POST {sandbox}/resume",
            $"POST {sandbox}/rebuild",
            $"POST {sandbox}/turn-off",
            $"POST {sandbox}/screen",
            $"POST {sandbox}/input",
            $"POST {sandbox}/commands");
        server.AntiforgeryTokens.Skip(1).Should().AllBe("sample-antiforgery-token");
        JsonElement.Parse(server.Requests[6].Body).GetProperty("keys")[0].GetString().Should().Be("enter");
        JsonElement.Parse(server.Requests[7].Body).GetProperty("command").GetString().Should().Be("echo ok");
    }

    [Fact]
    public async Task A_bots_screen_reader_is_sent_with_its_sandbox_settings_and_read_back()
    {
        var spaceId = Guid.NewGuid();
        var botId = Guid.NewGuid();
        var connectionId = Guid.NewGuid();
        var at = $"/api/spaces/{spaceId}/bots/{botId}/definition";
        var settings = new SandboxSettingsBody("off", [], null, Desktop: true, new ScreenReaderBody(connectionId, "sample-eyes"));
        var detail = new BotDetail(
            botId, "Sample", "sample", new BotAvatarBody(null, null), string.Empty, string.Empty, Guid.NewGuid(), "sample-plain",
            new ModelSettingsBody(null, null, null, "off", false), new MemorySettingsBody(false, "none", null, null), "private",
            settings, [], "active", null, 0, new BuildLimitsBody(null, null), null, null);
        server.Answer("PUT", at, detail);
        var request = new UpdateBotRequest(null, null, null, null, null, null, null, null, null, null, settings, null, null);

        var saved = await server.CreateApi().UpdateBotDefinition(spaceId, botId, request, Xunit.TestContext.Current.CancellationToken);

        saved.SandboxSettings.ScreenReader.Should().Be(new ScreenReaderBody(connectionId, "sample-eyes"));
        var sent = JsonElement.Parse(server.Requests.Single().Body).GetProperty("sandboxSettings").GetProperty("screenReader");
        sent.GetProperty("modelConnectionId").GetGuid().Should().Be(connectionId);
        sent.GetProperty("model").GetString().Should().Be("sample-eyes");
    }

    [Fact]
    public async Task A_refused_screen_carries_the_servers_own_sentence()
    {
        var spaceId = Guid.NewGuid();
        var botId = Guid.NewGuid();
        server.Refuse("POST", $"/api/spaces/{spaceId}/bots/{botId}/sandbox/screen", HttpStatusCode.Conflict, "This bot's sandbox has no screen.");

        var screen = () => server.CreateApi().GetBotSandboxScreen(spaceId, botId, Xunit.TestContext.Current.CancellationToken);

        (await screen.Should().ThrowAsync<DroverApiException>()).Which.Message.Should().Be("This bot's sandbox has no screen.");
    }

    #endregion
}
