namespace Drover.Server.Tests;

/// <summary>
/// Written rule patterns are off in every preset, so a server as it starts saves none and runs none. A rule that
/// has one all the same is not run as written: it takes its effect, and whoever reads it back is told why.
/// </summary>
public sealed class WrittenPatternSwitchTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    // A pattern no other test builds, so that finding it built would mean this path built it.
    private const string Stored = @"STORED-ORDER-\d+";

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Tests

    [Fact]
    public async Task As_the_server_starts_no_written_pattern_is_saved_or_tried_and_the_other_ways_to_look_for_text_stay()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        var bot = await NewBot(client, alice.SpaceId);
        var pattern = new BotRuleBody("No order numbers", true, "before_reply", "block", [new("text_matches") { Pattern = @"ORD-\d+" }]);

        var options = await Read<BotRuleOptions>(await client.Get($"{bot}/rules/options"), HttpStatusCode.OK);
        var saved = await client.Post($"{bot}/rules", pattern);
        var tried = await client.Post($"{bot}/rules/try", new TryRulesRequest("before_reply", null, null, "ORD-1", pattern));
        var contains = await client.Post($"{bot}/rules", new BotRuleBody("No order prefix", true, "before_reply", "block", [new("text_contains") { Entries = ["ORD-"] }]));
        var builtIn = await client.Post($"{bot}/rules", new BotRuleBody("No keys", true, "before_reply", "redact", [new("text_holds") { Builtin = "key" }]));

        options.WrittenPatterns.Should().BeFalse();
        saved.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(saved)).Should().Contain("AllowWrittenRulePatterns", "the message names the rule an operator would change");
        tried.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(tried)).Should().Contain("AllowWrittenRulePatterns");
        contains.StatusCode.Should().Be(HttpStatusCode.Created);
        builtIn.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_rule_that_has_a_written_pattern_is_never_built_or_run_takes_its_effect_and_says_why()
    {
        var alice = await SeedPerson("alice");
        var client = NewClient();
        (await client.SignIn(alice.Email, alice.Password)).EnsureSuccessStatusCode();
        var bot = await NewBot(client, alice.SpaceId);
        var botId = Guid.Parse(bot[(bot.LastIndexOf('/') + 1)..]);
        await SeedRule(alice.SpaceId, alice.UserId, botId, Stored);

        var listed = await Read<List<BotRuleResponse>>(await client.Get($"{bot}/rules"), HttpStatusCode.OK);
        var trial = await Read<TryRulesResponse>(await client.Post($"{bot}/rules/try", new TryRulesRequest("before_reply", null, null, "Nothing like an order number here.")), HttpStatusCode.OK);

        listed.Should().ContainSingle().Which.Notes.Should().ContainSingle().Which.Should().Contain("AllowWrittenRulePatterns").And.Contain("applies to everything");
        trial.Effect.Should().Be("block", "a pattern that is not run was not checked, and what was not checked is not let through");
        trial.Why.Should().Contain("written patterns are turned off on this server");
        RulePatterns.IsKept(Stored).Should().BeFalse("reading the rule back and trying it built no expression");
    }

    #endregion

    #region Private Methods

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private static async Task<T> Read<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<string> NewBot(ApiClient client, Guid spaceId)
    {
        var connection = await Read<ModelConnectionSummary>(
            await client.Post($"/api/spaces/{spaceId}/model-connections", new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "For Helper", null, null, "scripted")),
            HttpStatusCode.Created);
        var bot = await Read<BotSummary>(await client.Post($"/api/spaces/{spaceId}/bots", new CreateBotRequest("Helper", "Answer briefly.", connection.Id, null)), HttpStatusCode.Created);
        return $"/api/spaces/{spaceId}/bots/{bot.Id}";
    }

    // A rule as it was saved while written patterns were on.
    private async Task SeedRule(Guid spaceId, Guid userId, Guid botId, string pattern)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var tenant = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        scope.ServiceProvider.GetRequiredService<BotRuleRepository>().Add(new BotRule
        {
            BotId = botId,
            Name = "No order numbers",
            Enabled = true,
            Checkpoint = RuleCheckpoint.BeforeReply,
            Effect = RuleEffect.Block,
            Conditions = [new RuleCondition { Kind = RuleConditionKind.TextMatches, Pattern = pattern }],
            CreatedByUserId = userId,
        });
        await tenant.Commit(Token);
    }

    #endregion
}
