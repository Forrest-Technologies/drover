namespace Drover.Adapters.Tests;

/// <summary>
/// The ChatGPT plan signed in by code, on the wire: the sign-in's requests against an in-memory authorization server,
/// and a person's calls against a scripted handler. Shapes and addresses from the provider's command line tool's
/// published source (its login crate's device code, OAuth client, auth manager and revoke modules; its API crate's model
/// list, header names and error mapping), read 2026-11-27. Every token here is made by the test.
/// </summary>
public sealed class OpenAIPlanCodeWireTests
{
    #region Private Fields

    private const string Model = "gpt-test-coder";

    #endregion

    #region Signing In

    [Fact]
    public async Task Starting_asks_for_a_code_with_the_tools_client_id_and_names_the_page_the_interval_and_the_lifetime()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);

        var step = await signIn.Begin(Attempt(), Token);

        step.Wait.Should().Be(ModelSignInWait.Poll);
        step.Address.Should().Be(new Uri("https://auth.openai.com/codex/device"));
        step.Code.Should().NotBeNullOrWhiteSpace();
        step.Interval.Should().Be(TimeSpan.FromSeconds(7), "the provider sends its interval as text");
        step.Lifetime.Should().Be(TimeSpan.FromMinutes(15));
        step.Kept.Should().ContainKey("device_auth_id");
        var start = server.Sent.Single();
        start.Address.Should().Be(new Uri("https://auth.openai.com/api/accounts/deviceauth/usercode"));
        start.IsJson.Should().BeTrue();
        start.Fields.Should().Equal(new Dictionary<string, string> { ["client_id"] = "app_EMoamEEZ73f0CkXaXp7hrann" });
    }

    [Fact]
    public async Task A_poll_waits_while_pending_slows_down_when_told_and_exchanges_the_handed_back_code_once()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var attempt = await Begun(signIn);

        (await signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token)).Should().BeNull();
        server.Device = DeviceAnswer.SlowDown;
        await FluentActions.Awaiting(() => signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token)).Should().ThrowAsync<ModelSignInSlowDownException>();
        server.Device = DeviceAnswer.Approved;
        var tokens = await signIn.Finish(attempt, new ModelSignInReturn(string.Empty), Token);

        tokens.Should().NotBeNull();
        tokens!.AccountLabel.Should().Be(OpenAIPlanAuthServer.Email);
        tokens.Details.Should().Contain(OpenAIPlanSignIn.FlowDetail, OpenAIPlanSignIn.CodeFlow).And.Contain(OpenAIPlanSignIn.AccountDetail, OpenAIPlanAuthServer.AccountId);
        tokens.ExpiresAt.Should().NotBeNull();
        var polls = server.Sent.Where(form => form.Address == new Uri("https://auth.openai.com/api/accounts/deviceauth/token")).ToList();
        polls.Should().HaveCount(3).And.AllSatisfy(poll =>
        {
            poll.IsJson.Should().BeTrue();
            poll.Fields.Keys.Should().BeEquivalentTo(["device_auth_id", "user_code"]);
        });
        var exchange = server.Sent.Single(form => form.Address == new Uri("https://auth.openai.com/oauth/token"));
        exchange.IsJson.Should().BeFalse("the exchange is form encoded");
        exchange.Field("grant_type").Should().Be("authorization_code");
        exchange.Field("client_id").Should().Be("app_EMoamEEZ73f0CkXaXp7hrann");
        exchange.Field("redirect_uri").Should().Be("https://auth.openai.com/deviceauth/callback");
        exchange.Field("code_verifier").Should().NotBeNullOrEmpty("the verifier is the one the provider handed back");
    }

    [Fact]
    public async Task A_provider_that_will_not_give_a_code_says_to_turn_on_device_code_sign_in()
    {
        var server = new OpenAIPlanAuthServer { CodeStartRefused = true };

        var starting = () => ObservedPlanSignIn.Over(server).Begin(Attempt(), Token);

        (await starting.Should().ThrowAsync<ModelSignInRefusedException>()).Which.Message.Should().Be(OpenAIPlanSignIn.CodeRefused);
    }

    [Fact]
    public async Task A_refresh_by_code_is_json_rotates_and_one_for_another_account_ends_the_sign_in()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var first = await SignedIn(signIn, server);

        var second = await signIn.Refresh(first, Token);
        server.RefreshAccountId = "acct-someone-else";
        var refreshing = () => signIn.Refresh(second, Token);

        second.RefreshToken.Should().NotBe(first.RefreshToken);
        second.Details.Should().Equal(first.Details);
        second.AccountLabel.Should().Be(first.AccountLabel);
        await refreshing.Should().ThrowAsync<SignInEndedException>("a person's calls never go out as another account");
        var refresh = server.Sent.First(form => form.Field("grant_type") == "refresh_token");
        refresh.IsJson.Should().BeTrue();
        refresh.Fields.Keys.Should().BeEquivalentTo(["client_id", "grant_type", "refresh_token"]);
    }

    [Fact]
    public async Task Signing_out_by_code_revokes_the_refresh_token_as_json()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var tokens = await SignedIn(signIn, server);

        await signIn.Revoke(tokens, Token);

        var revoke = server.Sent.Single(form => form.Address == new Uri("https://auth.openai.com/oauth/revoke"));
        revoke.IsJson.Should().BeTrue();
        revoke.Field("token").Should().Be(tokens.RefreshToken);
        revoke.Field("token_type_hint").Should().Be("refresh_token");
    }

    [Fact]
    public async Task What_the_browser_sign_in_registered_is_kept_beside_a_sign_in_by_code()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        var earlier = new Dictionary<string, string>
        {
            [OpenAIPlanSignIn.ClientIdDetail] = OpenAIPlanAuthServer.IssuedClientId,
            [OpenAIPlanSignIn.HostIdDetail] = "urn:uuid:00000000-0000-4000-8000-000000000001",
            [OpenAIPlanSignIn.SubjectDetail] = OpenAIPlanAuthServer.Subject,
        };

        var tokens = await SignedIn(signIn, server, Attempt() with { Earlier = earlier });

        tokens.Details.Should().Contain(earlier).And.Contain(OpenAIPlanSignIn.FlowDetail, OpenAIPlanSignIn.CodeFlow);
    }

    [Fact]
    public async Task Changing_the_code_settings_changes_the_next_request_with_no_rebuild()
    {
        var server = new OpenAIPlanAuthServer();
        var signIn = ObservedPlanSignIn.Over(server);
        server.Settings.Code.ClientId = "app_changed_by_the_operator";
        server.Settings.Code.StartAddress = new Uri("https://auth.example.test/deviceauth/usercode");

        await signIn.Begin(Attempt(), Token);

        var start = server.Sent.Single();
        start.Address.Should().Be(server.Settings.Code.StartAddress);
        start.Field("client_id").Should().Be("app_changed_by_the_operator");
    }

    #endregion

    #region Calling

    [Fact]
    public async Task Runs_go_to_the_codex_address_with_the_flows_headers_the_account_and_the_tools_request_shape()
    {
        var token = AccessToken("acct-from-the-token");
        var wire = new Wire(Stream(TextDeltas("Hi.")), Stream(TextDeltas("Hi.")));
        var client = wire.Provider().CreateChatClient(ByCode(), token, Model, new ModelSettings { Temperature = 0.4 });
        var weather = new ModelTool("get_weather", "Weather for a city", JsonSerializer.SerializeToElement(new { type = "object" }));
        var request = new ModelChatRequest(
            [new ModelChatMessage("system", [new ModelText("Answer in French.")]), new ModelChatMessage("user", [new ModelText("Say hello.")])],
            new ModelChatOptions { Tools = [weather] });

        await client.GetResponse(request, Token);
        await Drain(client.GetStreamingResponse(request, Token));

        wire.Sent.Should().HaveCount(2).And.AllSatisfy(sent =>
        {
            sent.RequestUri.Should().Be(new Uri("https://chatgpt.com/backend-api/codex/responses"));
            sent.Headers.Authorization!.Parameter.Should().Be(token);
            sent.Headers.GetValues("chatgpt-account-id").Should().Equal("acct-from-the-token");
            sent.Headers.GetValues("originator").Should().Equal("drover");
            sent.Headers.GetValues("OpenAI-Beta").Should().Equal("responses=experimental");
        });
        wire.Requests.Should().AllSatisfy(sent =>
        {
            sent.Json.GetProperty("store").GetBoolean().Should().BeFalse();
            sent.Json.GetProperty("stream").GetBoolean().Should().BeTrue();
            sent.Json.GetProperty("include").EnumerateArray().Select(item => item.GetString()).Should().Equal("reasoning.encrypted_content");
            sent.Json.GetProperty("instructions").GetString().Should().Contain("Answer in French.");
            sent.Json.TryGetProperty("temperature", out _).Should().BeFalse();
            sent.Json.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("type").GetString()).Should().Equal("function");
        });
    }

    [Fact]
    public async Task The_model_list_asks_the_codex_address_with_the_tool_version_and_the_account_for_json()
    {
        var token = AccessToken("acct-from-the-token");
        var wire = new Wire(new Answer(HttpStatusCode.OK, "application/json", """{"models":[{"slug":"gpt-test-coder","display_name":"GPT Test Coder","visibility":"list"},{"slug":"hidden","visibility":"hide"}]}"""));

        var models = await wire.Provider().ListModels(ByCode(), token, Token);

        models.Select(model => model.Id).Should().Equal("gpt-test-coder");
        var sent = wire.Sent.Single();
        sent.RequestUri.Should().Be(new Uri("https://chatgpt.com/backend-api/codex/models?client_version=0.159.3"));
        sent.Headers.GetValues("chatgpt-account-id").Should().Equal("acct-from-the-token");
        sent.Headers.Accept.Select(accept => accept.MediaType).Should().Equal("application/json");
    }

    [Fact]
    public async Task A_token_that_names_no_account_is_sent_with_the_account_its_sign_in_recorded()
    {
        var wire = new Wire(new Answer(HttpStatusCode.OK, "application/json", """{"models":[]}"""));

        await wire.Provider().ListModels(ByCode(), TestKeys.Generate(), Token);

        wire.Sent.Single().Headers.GetValues("chatgpt-account-id").Should().Equal(OpenAIPlanAuthServer.AccountId);
    }

    [Theory]
    [InlineData("usage_limit_reached", ModelFailure.OutOfCredit)]
    [InlineData("usage_not_included", ModelFailure.OutOfCredit)]
    [InlineData("something_else", ModelFailure.RetryLater)]
    public async Task A_limit_answer_is_out_of_credit_and_any_other_429_is_a_wait(string type, ModelFailure expected)
    {
        var body = new JsonObject { ["error"] = new JsonObject { ["type"] = type, ["message"] = "Made-up message for a test." } }.ToJsonString();
        var wire = new Wire(new Answer(HttpStatusCode.TooManyRequests, "application/json", body));
        var client = wire.Provider().CreateChatClient(ByCode(), AccessToken("acct-test"), Model, new ModelSettings());

        var failure = (await FluentActions.Awaiting(() => client.GetResponse(Prompt("Say hello."), Token)).Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(expected);
        failure.Message.Should().NotContain("Made-up message");
    }

    [Fact]
    public async Task A_limit_inside_the_stream_is_out_of_credit()
    {
        var failed = Event("response.failed", new { response = new { id = "resp_code", @object = "response", created_at = 1794400000, status = "failed", model = Model, output = Array.Empty<object>(), error = new { code = "usage_limit_reached", message = "Made-up message for a test." } } });
        var wire = new Wire(Stream([Event("response.created", new { response = new { id = "resp_code", @object = "response", created_at = 1794400000, status = "in_progress", model = Model, output = Array.Empty<object>() } }), failed]));
        var client = wire.Provider().CreateChatClient(ByCode(), AccessToken("acct-test"), Model, new ModelSettings());

        var failure = (await FluentActions.Awaiting(() => Drain(client.GetStreamingResponse(Prompt("Say hello."), Token))).Should().ThrowAsync<ModelProviderException>()).Which;

        failure.Failure.Should().Be(ModelFailure.OutOfCredit);
        failure.Message.Should().Be(PlanFailures.LimitReached);
    }

    [Fact]
    public async Task Changing_the_code_api_address_changes_the_next_request_with_no_rebuild()
    {
        var settings = new OpenAIPlanOptions();
        var wire = new Wire(new Answer(HttpStatusCode.OK, "application/json", """{"models":[]}"""));
        settings.Code.ApiAddress = new Uri("https://backend.example.test/api/");
        settings.Code.ClientVersion = "1.2.3";

        await wire.Provider(new SettableOptions<OpenAIPlanOptions>(settings)).ListModels(ByCode(), AccessToken("acct-test"), Token);

        wire.Sent.Single().RequestUri.Should().Be(new Uri("https://backend.example.test/api/codex/models?client_version=1.2.3"));
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelEndpoint ByCode() =>
        new(OpenAIPlanProvider.ConnectionKind, new OpenAIPlanOptions().ApiAddress)
        {
            SignInDetails = new Dictionary<string, string>
            {
                [OpenAIPlanSignIn.FlowDetail] = OpenAIPlanSignIn.CodeFlow,
                [OpenAIPlanSignIn.AccountDetail] = OpenAIPlanAuthServer.AccountId,
            },
        };

    /// <summary>A token shaped like the flow's: a JSON web token whose auth claim names the account. Made here, signed by nothing.</summary>
    private static string AccessToken(string account)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"none"}"""));
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(new JsonObject
        {
            ["https://api.openai.com/auth"] = new JsonObject { ["chatgpt_account_id"] = account },
            ["jti"] = TestKeys.Generate(),
        }.ToJsonString()));
        return $"{header}.{payload}.{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16))}";
    }

    private static ModelSignInAttempt Attempt() => new(Guid.NewGuid(), Random(), Random(), Random(), null);

    private static async Task<ModelSignInAttempt> Begun(ObservedPlanSignIn signIn, ModelSignInAttempt? attempt = null)
    {
        var started = attempt ?? Attempt();
        var step = await signIn.Begin(started, Token);
        return started with { Begun = step.Kept };
    }

    private static async Task<ModelTokens> SignedIn(ObservedPlanSignIn signIn, OpenAIPlanAuthServer server, ModelSignInAttempt? attempt = null)
    {
        var begun = await Begun(signIn, attempt);
        server.Device = DeviceAnswer.Approved;
        return await signIn.Finish(begun, new ModelSignInReturn(string.Empty), Token) ?? throw new InvalidOperationException("No token set.");
    }

    private static string Random() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    private static ModelChatRequest Prompt(string text) => new([new ModelChatMessage("user", [new ModelText(text)])], new ModelChatOptions());

    private static async Task Drain(IAsyncEnumerable<ModelChatUpdate> updates)
    {
        await foreach (var _ in updates)
        {
        }
    }

    private static string Event(string type, object fields)
    {
        var json = JsonSerializer.SerializeToNode(fields)!.AsObject();
        json["type"] = type;
        json["sequence_number"] = 0;
        return json.ToJsonString();
    }

    private static IEnumerable<string> TextDeltas(string text)
    {
        object message = new { type = "message", id = "msg_code", role = "assistant", status = "completed", content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } } };
        object Response(string status, object[] output) => new { id = "resp_code", @object = "response", created_at = 1794400000, status, model = Model, output, usage = new { input_tokens = 5, output_tokens = 2, total_tokens = 7 } };
        yield return Event("response.created", new { response = Response("in_progress", []) });
        yield return Event("response.output_text.delta", new { item_id = "msg_code", output_index = 0, content_index = 0, delta = text });
        yield return Event("response.output_item.done", new { output_index = 0, item = message });
        yield return Event("response.completed", new { response = Response("completed", [message]) });
    }

    private static Answer Stream(IEnumerable<string> events) =>
        new(HttpStatusCode.OK, "text/event-stream", string.Concat(events.Select(@event => $"data: {@event}\n\n")));

    #endregion

    #region Nested Types

    private sealed record Answer(HttpStatusCode Status, string ContentType, string Body);

    /// <summary>Answers each request with the next scripted answer, in order, and keeps what was sent.</summary>
    private sealed class Wire
    {
        #region Private Fields

        private readonly Answer[] answers;

        #endregion

        #region Constructors

        public Wire(params Answer[] answers)
        {
            this.answers = answers;
            Handler = new ScriptedHandler(Answer);
        }

        #endregion

        #region Properties

        public ScriptedHandler Handler { get; }

        public List<HttpRequestMessage> Sent { get; } = [];

        public List<WireRequest> Requests { get; } = [];

        #endregion

        #region Public Methods

        public OpenAIPlanProvider Provider(IOptionsMonitor<OpenAIPlanOptions>? settings = null) =>
            new(new FixtureHttpClientFactory(Handler), settings ?? new SettableOptions<OpenAIPlanOptions>(new()));

        #endregion

        #region Private Methods

        private async Task<HttpResponseMessage> Answer(HttpRequestMessage request, int count, CancellationToken cancellation)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellation);
            Sent.Add(request);
            Requests.Add(new WireRequest(request.Method.Method, request.RequestUri!, body));
            var answer = answers[count - 1];
            HttpContent content = answer.ContentType == "text/event-stream"
                ? new StreamContent(new NonSeekableStream(new MemoryStream(Encoding.UTF8.GetBytes(answer.Body))))
                : new StringContent(answer.Body, Encoding.UTF8);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(answer.ContentType);
            return new HttpResponseMessage(answer.Status) { Content = content };
        }

        #endregion
    }

    #endregion
}
