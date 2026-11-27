namespace Drover.Adapters.Tests;

/// <summary>
/// What a call on a Claude plan connection puts on the wire: the key kind's fixed address, the person's token as the
/// bearer credential and no <c>x-api-key</c>, the configured headers on every request, and the configured prefix as
/// the first system block with the bot's instructions as the second. The shapes are hand-written from
/// https://platform.claude.com/docs/en/api/messages and the provider's working client (see
/// <see cref="AnthropicPlanSignInOptions"/>).
/// </summary>
public sealed class AnthropicPlanWireTests
{
    #region Private Fields

    private const string Model = "claude-test-model";

    private static readonly ModelEndpoint connection = new(AnthropicPlanSignIn.ConnectionKind, AnthropicProvider.BaseAddress);

    #endregion

    #region Requests

    [Fact]
    public async Task Every_request_carries_the_token_as_a_bearer_the_headers_and_no_api_key()
    {
        var server = new MessagesServer();
        var options = new SettableOptions<AnthropicPlanSignInOptions>(new AnthropicPlanSignInOptions());
        var provider = new AnthropicPlanProvider(new FixtureHttpClientFactory(server.Handler), options);
        var token = TestKeys.Generate();

        await provider.CreateChatClient(connection, token, Model, new ModelSettings()).GetResponse(Prompt("Say hello.", "Be brief."), Token);
        await provider.ListModels(connection, token, Token);
        await provider.TestConnection(connection, token, Token);

        server.Sent.Should().HaveCount(4);
        server.Sent.Should().OnlyContain(request => request.RequestUri!.Host == "api.anthropic.com");
        server.Sent.Should().OnlyContain(request => request.Headers.Authorization!.Scheme == "Bearer" && request.Headers.Authorization.Parameter == token);
        server.Sent.Should().OnlyContain(request => !request.Headers.Contains("x-api-key"));
        server.Sent.Should().OnlyContain(request => request.Headers.GetValues("anthropic-beta").Single() == "claude-code-20250219,oauth-2025-04-20");
        server.Sent.Should().OnlyContain(request => request.Headers.GetValues("x-app").Single() == "cli");
        server.Sent.Should().OnlyContain(request => string.Join(' ', request.Headers.GetValues("user-agent")).Contains("claude-cli/2.1.322", StringComparison.Ordinal));
        server.Sent[1].RequestUri!.AbsolutePath.Should().Be("/v1/messages");
    }

    [Fact]
    public async Task The_prefix_is_the_first_system_block_and_the_instructions_the_second()
    {
        var server = new MessagesServer();
        var provider = new AnthropicPlanProvider(new FixtureHttpClientFactory(server.Handler), new SettableOptions<AnthropicPlanSignInOptions>(new AnthropicPlanSignInOptions()));

        await provider.CreateChatClient(connection, TestKeys.Generate(), Model, new ModelSettings()).GetResponse(Prompt("Say hello.", "Be brief."), Token);

        var system = server.Messages.Single().GetProperty("system").EnumerateArray().ToList();
        system.Should().HaveCount(2);
        system[0].GetProperty("type").GetString().Should().Be("text");
        system[0].GetProperty("text").GetString().Should().Be("You are Claude Code, Anthropic's official CLI for Claude.");
        system[1].GetProperty("text").GetString().Should().Be("Be brief.");
    }

    [Fact]
    public async Task With_no_instructions_the_prefix_is_the_only_system_block()
    {
        var server = new MessagesServer();
        var provider = new AnthropicPlanProvider(new FixtureHttpClientFactory(server.Handler), new SettableOptions<AnthropicPlanSignInOptions>(new AnthropicPlanSignInOptions()));

        await provider.CreateChatClient(connection, TestKeys.Generate(), Model, new ModelSettings()).GetResponse(Prompt("Say hello.", null), Token);

        server.Messages.Single().GetProperty("system").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("text").GetString().Should().Be("You are Claude Code, Anthropic's official CLI for Claude.");
    }

    [Fact]
    public async Task A_changed_header_and_prefix_reach_the_next_client_without_a_rebuild()
    {
        var server = new MessagesServer();
        var options = new SettableOptions<AnthropicPlanSignInOptions>(new AnthropicPlanSignInOptions());
        var provider = new AnthropicPlanProvider(new FixtureHttpClientFactory(server.Handler), options);
        await provider.CreateChatClient(connection, TestKeys.Generate(), Model, new ModelSettings()).GetResponse(Prompt("One.", null), Token);

        options.CurrentValue = new AnthropicPlanSignInOptions
        {
            Headers = new(StringComparer.OrdinalIgnoreCase) { ["anthropic-beta"] = "changed-beta", ["x-app"] = "cli" },
            SystemPrefix = "A changed prefix.",
        };
        await provider.CreateChatClient(connection, TestKeys.Generate(), Model, new ModelSettings()).GetResponse(Prompt("Two.", null), Token);

        server.Sent[^1].Headers.GetValues("anthropic-beta").Single().Should().Be("changed-beta");
        server.Messages[^1].GetProperty("system")[0].GetProperty("text").GetString().Should().Be("A changed prefix.");
    }

    [Fact]
    public async Task A_call_with_no_token_is_refused_before_anything_is_sent()
    {
        var server = new MessagesServer();
        var provider = new AnthropicPlanProvider(new FixtureHttpClientFactory(server.Handler), new SettableOptions<AnthropicPlanSignInOptions>(new AnthropicPlanSignInOptions()));

        var test = await provider.TestConnection(connection, null, Token);
        var listing = () => provider.ListModels(connection, null, Token);

        test.Failure.Should().Be(ModelFailure.InvalidKey);
        await listing.Should().ThrowAsync<ModelProviderException>();
        server.Sent.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static ModelChatRequest Prompt(string text, string? instructions) =>
        new([new ModelChatMessage("user", [new ModelText(text)])], new ModelChatOptions { Instructions = instructions });

    #endregion

    #region Nested Types

    /// <summary>Answers the model's own entry, the model list, and each Messages call with a short reply, and keeps what was sent.</summary>
    private sealed class MessagesServer
    {
        #region Private Fields

        private static readonly object entry = new
        {
            type = "model",
            id = Model,
            display_name = "Claude Test Model",
            max_input_tokens = 200_000,
            max_tokens = 8_000,
            capabilities = new { image_input = new { supported = true } },
        };

        #endregion

        #region Constructors

        public MessagesServer() => Handler = new ScriptedHandler(Answer);

        #endregion

        #region Properties

        public ScriptedHandler Handler { get; }

        public List<HttpRequestMessage> Sent { get; } = [];

        /// <summary>The bodies of the Messages calls.</summary>
        public List<JsonElement> Messages { get; } = [];

        #endregion

        #region Private Methods

        private async Task<HttpResponseMessage> Answer(HttpRequestMessage request, int count, CancellationToken cancellation)
        {
            Sent.Add(request);

            if (request.Method == HttpMethod.Get)
            {
                var listed = request.RequestUri!.AbsolutePath.EndsWith("/v1/models", StringComparison.Ordinal);
                return ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(listed ? new { data = new[] { entry }, has_more = false } : entry));
            }

            Messages.Add(JsonElement.Parse(await request.Content!.ReadAsStringAsync(cancellation)));
            return ScriptedHandler.Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
            {
                id = "msg_plan_1",
                type = "message",
                role = "assistant",
                model = Model,
                content = new[] { new { type = "text", text = "Hello." } },
                stop_reason = "end_turn",
                stop_sequence = (string?)null,
                usage = new { input_tokens = 10, output_tokens = 2, cache_creation_input_tokens = 0, cache_read_input_tokens = 0 },
            }));
        }

        #endregion
    }

    #endregion
}
