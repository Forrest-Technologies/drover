namespace Drover.Server.Tests;

/// <summary>
/// A server whose operator turned off rules written as a sentence (deployment rule AllowJudgedRules) saves none, and
/// one stored earlier is never sent to a decision model: it counts as unsure, and whoever reads it back is told why.
/// The decision model is scripted, so a request it was sent would show. Every value here is a sample.
/// </summary>
public sealed class JudgedRuleSwitchTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private readonly ScriptedDecisionModel judge = new() { Applies = 0.01 };

    private ScriptedModelProvider? model;

    #endregion

    #region Properties

    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>(base.Settings)
    {
        [$"Drover:Policy:Rules:{nameof(DeploymentPolicy.AllowJudgedRules)}"] = "false",
    };

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));
        services.AddSingleton<FindDecisionModel>(_ => (_, _, _) => Task.FromResult<SpaceDecisionModel?>(new(judge, "scripted-judge", null)));
    }

    #endregion

    #region Tests

    [Fact]
    public async Task A_sentence_rule_is_refused_at_save_and_as_a_draft_with_the_rule_named()
    {
        var (client, bot, _) = await SignedInWithBot();
        var rule = new BotRuleBody("Ask before mail", true, "before_tool", "ask", [new("judged") { Statement = "Always ask me before sending an email." }]);

        var saved = await client.Post($"{bot}/rules", rule);
        var tried = await client.Post($"{bot}/rules/try", new TryRulesRequest("before_tool", "email.send", "{}", null, rule));

        saved.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(saved)).Should().Contain(nameof(DeploymentPolicy.AllowJudgedRules));
        tried.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(tried)).Should().Contain(nameof(DeploymentPolicy.AllowJudgedRules));
    }

    [Fact]
    public async Task A_stored_sentence_rule_is_never_sent_counts_as_unsure_and_says_why()
    {
        var (client, bot, person) = await SignedInWithBot();
        var botId = Guid.Parse(bot[(bot.LastIndexOf('/') + 1)..]);
        await InSpace(person.SpaceId, person.UserId, async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            db.BotRules.Add(new BotRule
            {
                BotId = botId,
                Name = "Saved earlier",
                Checkpoint = RuleCheckpoint.BeforeTool,
                Effect = RuleEffect.Stop,
                Conditions = [new RuleCondition { Kind = RuleConditionKind.Judged, Statement = "Never pay twice." }],
            });
            await db.SaveChangesAsync(Token);
            return true;
        });

        var listed = await Read<List<BotRuleResponse>>(await client.Get($"{bot}/rules"));
        var trial = await Read<TryRulesResponse>(await client.Post($"{bot}/rules/try", new TryRulesRequest("before_tool", "invoice.pay", "{}", null)));

        listed.Single().Notes.Should().ContainSingle().Which.Should().Contain(nameof(DeploymentPolicy.AllowJudgedRules)).And.Contain("never checked");
        trial.Effect.Should().Be("ask", "a rule that is not checked goes to a person");
        trial.Why.Should().Contain("turned off on this server");
        judge.Requests.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private async Task<(ApiClient Client, string Bot, (string Email, string Password, Guid UserId, Guid SpaceId) Person)> SignedInWithBot()
    {
        var person = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(person.Email, person.Password)).EnsureSuccessStatusCode();
        var space = $"/api/spaces/{person.SpaceId}";
        var connection = await Read<ModelConnectionSummary>(
            await client.Post($"{space}/model-connections", new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "For Helper", null, null, "scripted")),
            HttpStatusCode.Created);
        var bot = await Read<BotSummary>(await client.Post($"{space}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)), HttpStatusCode.Created);
        return (client, $"{space}/bots/{bot.Id}", person);
    }

    private async Task<T> InSpace<T>(Guid spaceId, Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var tenant = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await work(scope.ServiceProvider);
        await tenant.Commit(Token);
        return result;
    }

    #endregion
}
