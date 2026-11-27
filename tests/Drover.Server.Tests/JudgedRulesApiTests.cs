namespace Drover.Server.Tests;

/// <summary>
/// Rules written as a sentence through the rules API over real PostgreSQL: what is accepted and refused at save, what
/// the read-back says, and that "Try it" runs the same two steps a run does and answers with the outcome and the
/// probability. The decision model is scripted. Every value here is a sample.
/// </summary>
public sealed class JudgedRulesApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private const string AskFirst = "Always ask me before sending an email.";

    private readonly ScriptedDecisionModel judge = new();

    private bool connected = true;

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<FindDecisionModel>(_ => (_, _) => Task.FromResult<SpaceDecisionModel?>(connected ? new(judge, "scripted-judge", null) : null));
    }

    #endregion

    #region Saving

    [Fact]
    public async Task A_sentence_rule_is_saved_and_read_back_with_its_numbers_and_what_it_is_not()
    {
        var (client, bot) = await SignedInWithBot();

        var saved = await Send<BotRuleResponse>(client.Post($"{bot}/rules", AskBeforeMail(0.9)), HttpStatusCode.Created);

        var condition = saved.Conditions.Should().ContainSingle(read => read.Kind == "judged").Subject;
        condition.Statement.Should().Be(AskFirst);
        condition.MinConfidence.Should().Be(0.9);
        saved.Notes.Should().ContainSingle().Which.Should().Contain("not guaranteed");
    }

    [Fact]
    public async Task With_no_decision_model_the_read_back_says_the_rule_cannot_be_checked()
    {
        connected = false;
        var (client, bot) = await SignedInWithBot();
        await Send<BotRuleResponse>(client.Post($"{bot}/rules", AskBeforeMail()), HttpStatusCode.Created);

        var listed = await Send<List<BotRuleResponse>>(client.Get($"{bot}/rules"), HttpStatusCode.OK);

        listed.Single().Notes.Should().ContainSingle().Which.Should().Contain("No decision model is connected").And.Contain("a person is asked");
    }

    [Theory]
    [MemberData(nameof(RefusedRules))]
    public async Task A_sentence_rule_that_cannot_hold_is_refused_at_save_with_the_reason(string what, BotRuleBody body, string expected)
    {
        var (client, bot) = await SignedInWithBot();

        var refused = await client.Post($"{bot}/rules", body);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, what);
        (await Title(refused)).Should().Contain(expected, what);
    }

    public static TheoryData<string, BotRuleBody, string> RefusedRules() => new()
    {
        { "after a tool", new("Sample", true, "after_tool", "block", [Sentence()]), "before a tool is used or before the bot replies" },
        { "when a message arrives", new("Sample", true, "message_arrives", "stop", [Sentence()]), "before a tool is used or before the bot replies" },
        { "redacting", new("Sample", true, "before_reply", "redact", [Sentence()]), "cannot redact" },
        { "two sentences", new("Sample", true, "before_tool", "ask", [Sentence(), Sentence("Never pay twice.")]), "one sentence" },
        { "no sentence", new("Sample", true, "before_tool", "ask", [new("judged")]), "one sentence" },
        { "too sure", new("Sample", true, "before_tool", "ask", [Sentence() with { MinConfidence = 1.5 }]), "0.50 to 0.99" },
    };

    #endregion

    #region Try It

    [Theory]
    [InlineData(0.92, "applies", "ask")]
    [InlineData(0.05, "passes", null)]
    [InlineData(0.61, "unsure", "ask")]
    public async Task Try_it_runs_the_real_check_and_answers_with_the_outcome_and_the_probability(double applies, string judgment, string? effect)
    {
        judge.Applies = applies;
        var (client, bot) = await SignedInWithBot();
        await Send<BotRuleResponse>(client.Post($"{bot}/rules", AskFirstRule()), HttpStatusCode.Created);

        var tried = await Send<TryRulesResponse>(client.Post($"{bot}/rules/try", new TryRulesRequest("before_tool", "email.send", """{"to":"sam@example.org"}""", null)), HttpStatusCode.OK);

        tried.Judgment.Should().Be(judgment);
        tried.Probability.Should().Be(applies);
        tried.Effect.Should().Be(effect);
        judge.Requests.Should().ContainSingle().Which.State["action"].Should().Contain("sam@example.org");
        judge.ScopeOpen.Should().ContainSingle().Which.Should().BeFalse("the model is asked with no tenant scope open");
    }

    [Fact]
    public async Task Try_it_checks_a_draft_sentence_rule()
    {
        judge.Applies = 0.97;
        var (client, bot) = await SignedInWithBot();

        var tried = await Send<TryRulesResponse>(
            client.Post($"{bot}/rules/try", new TryRulesRequest("before_reply", null, null, "It ships tomorrow, promise.", new BotRuleBody("No promises", true, "before_reply", "block", [Sentence("Never promise a delivery date.")]))),
            HttpStatusCode.OK);

        tried.Judgment.Should().Be("applies");
        tried.Effect.Should().Be("block");
        tried.RuleName.Should().Be("No promises");
    }

    [Fact]
    public async Task With_no_decision_model_try_it_says_a_person_would_be_asked()
    {
        connected = false;
        var (client, bot) = await SignedInWithBot();
        await Send<BotRuleResponse>(client.Post($"{bot}/rules", AskFirstRule()), HttpStatusCode.Created);

        var tried = await Send<TryRulesResponse>(client.Post($"{bot}/rules/try", new TryRulesRequest("before_tool", "email.send", "{}", null)), HttpStatusCode.OK);

        tried.Judgment.Should().Be("unsure");
        tried.Probability.Should().BeNull();
        tried.Effect.Should().Be("ask");
        tried.Why.Should().Contain("no decision model is connected");
        judge.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Try_it_on_a_missing_bot_is_not_found()
    {
        var (client, bot) = await SignedInWithBot();
        var missing = $"{bot[..bot.LastIndexOf('/')]}/{Guid.CreateVersion7()}";

        var tried = await client.Post($"{missing}/rules/try", new TryRulesRequest("before_reply", null, null, "Sample"));

        tried.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Private Methods

    private static BotRuleBody AskFirstRule() => new("Ask before mail", true, "before_tool", "ask", [Sentence()]);

    private static BotRuleBody AskBeforeMail(double? confidence = null) =>
        new("Ask before mail", true, "before_tool", "ask", [new("tool_kind") { ToolKinds = ["write"] }, Sentence() with { MinConfidence = confidence }]);

    private static RuleConditionBody Sentence(string statement = AskFirst) => new("judged") { Statement = statement };

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private static async Task<T> Send<T>(Task<HttpResponseMessage> request, HttpStatusCode expected)
    {
        var response = await request;
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    /// <summary>A signed-in owner of a new space with one bot on a new connection, and the bot's address under the API.</summary>
    private async Task<(ApiClient Client, string Bot)> SignedInWithBot()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var space = $"/api/spaces/{person.SpaceId}";
        var connection = await Send<ModelConnectionSummary>(
            client.Post($"{space}/model-connections", new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "For Helper", null, null, "scripted")),
            HttpStatusCode.Created);
        var bot = await Send<BotSummary>(client.Post($"{space}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)), HttpStatusCode.Created);
        return (client, $"{space}/bots/{bot.Id}");
    }

    #endregion
}
