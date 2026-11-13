namespace Drover.Server.Tests;

/// <summary>
/// The bot rules API over real PostgreSQL: saving and ordering rules, what is refused at save, who may change them,
/// what a bot inherits, and that "Try it" answers with the evaluator a run uses. Every value here is a sample.
/// </summary>
public sealed class BotRulesApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Saving

    [Fact]
    public async Task A_rule_is_saved_read_back_changed_reordered_and_removed()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var rules = $"{await NewBot(client, alice.SpaceId)}/rules";

        var first = await Send<BotRuleResponse>(client.Post(rules, MailStaysInside()), HttpStatusCode.Created);
        var second = await Send<BotRuleResponse>(client.Post(rules, new BotRuleBody("No keys in replies", true, "before_reply", "redact", [new("text_holds") { Builtin = "key" }])), HttpStatusCode.Created);
        var moved = await Send<BotRuleResponse>(client.Put($"{rules}/{second.Id}", new BotRuleBody("No keys in replies", false, "before_reply", "redact", [new("text_holds") { Builtin = "key" }], Position: 0)), HttpStatusCode.OK);
        var listed = await Send<List<BotRuleResponse>>(client.Get(rules), HttpStatusCode.OK);
        var removed = await client.Delete($"{rules}/{second.Id}");
        var left = await Send<List<BotRuleResponse>>(client.Get(rules), HttpStatusCode.OK);
        var again = await client.Delete($"{rules}/{second.Id}");

        first.Should().Match<BotRuleResponse>(rule => rule.Name == "Mail stays inside" && rule.Enabled && rule.Position == 0 && rule.Checkpoint == "before_tool" && rule.Effect == "ask" && !rule.Inherited && rule.BotName == "Helper");
        first.Conditions.Should().BeEquivalentTo(MailStaysInside().Conditions);
        second.Position.Should().Be(1);
        moved.Should().Match<BotRuleResponse>(rule => rule.Position == 0 && !rule.Enabled);
        listed.Select(rule => (rule.Name, rule.Position)).Should().Equal(("No keys in replies", 0), ("Mail stays inside", 1));
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        left.Should().ContainSingle().Which.Should().Match<BotRuleResponse>(rule => rule.Id == first.Id && rule.Position == 0, "the rules left are numbered from zero again");
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(RefusedRules))]
    public async Task A_rule_that_cannot_be_enforced_as_written_is_refused_at_save_with_the_reason(string what, BotRuleBody body, string expected)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var rules = $"{await NewBot(client, alice.SpaceId)}/rules";

        var refused = await client.Post(rules, body);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, what);
        (await Title(refused)).Should().Contain(expected, what);
        (await Send<List<BotRuleResponse>>(client.Get(rules), HttpStatusCode.OK)).Should().BeEmpty("nothing is stored of a refused rule");
    }

    public static TheoryData<string, BotRuleBody, string> RefusedRules() => new()
    {
        { "no name", new(" ", true, "before_reply", "block", []), "Give the rule a name" },
        { "a misspelled moment", new("Sample", true, "sometimes", "block", []), "Choose when the rule is checked" },
        { "a misspelled effect", new("Sample", true, "before_reply", "allow", []), "Choose what the rule does" },
        { "a misspelled condition", new("Sample", true, "before_reply", "block", [new("mood_is")]), "not a kind of condition" },
        { "a misspelled tool kind", new("Sample", true, "before_tool", "block", [new("tool_kind") { ToolKinds = ["harmless"] }]), "reads, writes, or is critical" },
        { "ask at a reply", new("Sample", true, "before_reply", "ask", []), "only before a tool is used" },
        { "a pattern that could stall", new("Sample", true, "before_reply", "block", [new("text_matches") { Pattern = "(?=a)a" }]), "cannot stall" },
        { "a pattern too long", new("Sample", true, "before_reply", "block", [new("text_matches") { Pattern = new string('a', 501) }]), "up to 500 characters" },
        { "eleven conditions", new("Sample", true, "before_reply", "block", [.. Enumerable.Repeat(new RuleConditionBody("text_holds") { Builtin = "key" }, 11)]), "at most 10 conditions" },
        { "a null in a list", new("Sample", true, "before_reply", "block", [new("text_contains") { Entries = [null!] }]), "no blank entry" },
        { "a misspelled weekday", new("Sample", true, "before_reply", "block", [new("time_is") { From = "22:00", To = "07:00", TimeZone = "UTC", Weekdays = ["someday"] }]), "Choose each weekday once" },
    };

    [Fact]
    public async Task A_bot_has_at_most_fifty_rules_of_its_own()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var rules = $"{await NewBot(client, alice.SpaceId)}/rules";

        for (var number = 0; number < 50; number++)
        {
            (await client.Post(rules, new BotRuleBody($"Rule {number}", true, "before_reply", "block", [new("text_contains") { Entries = [$"sample {number}"] }]))).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var over = await client.Post(rules, new BotRuleBody("One too many", true, "before_reply", "block", []));

        over.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(over)).Should().Contain("at most 50 rules");
    }

    #endregion

    #region Access

    [Fact]
    public async Task Only_owners_and_admins_reach_a_bots_rules_and_another_space_sees_nothing()
    {
        var alice = await SeedPerson("alice");
        var outsider = await SeedPerson("outsider");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var asOwner = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var asOutsider = await SignIn(outsider.Email, outsider.Password);
        var bot = await NewBot(asOwner, alice.SpaceId);
        var rule = await Send<BotRuleResponse>(asOwner.Post($"{bot}/rules", MailStaysInside()), HttpStatusCode.Created);
        var elsewhere = bot.Replace(alice.SpaceId.ToString(), outsider.SpaceId.ToString(), StringComparison.Ordinal);

        List<(string What, Func<ApiClient, string, Task<HttpResponseMessage>> Call)> routes =
        [
            ("list rules", (client, at) => client.Get($"{at}/rules")),
            ("add a rule", (client, at) => client.Post($"{at}/rules", MailStaysInside())),
            ("change a rule", (client, at) => client.Put($"{at}/rules/{rule.Id}", MailStaysInside() with { Enabled = false })),
            ("try the rules", (client, at) => client.Post($"{at}/rules/try", new TryRulesRequest("before_reply", null, null, "Sample"))),
            ("remove a rule", (client, at) => client.Delete($"{at}/rules/{rule.Id}")),
        ];

        foreach (var (what, call) in routes)
        {
            (await call(asMember, bot)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"a member may not {what}");
            (await call(asOutsider, bot)).StatusCode.Should().Be(HttpStatusCode.NotFound, $"a person outside the space may not {what}");
            (await call(asOutsider, elsewhere)).StatusCode.Should().Be(HttpStatusCode.NotFound, $"a bot id from another space finds nothing to {what}");
        }

        var unsigned = await asOwner.PostWithoutToken($"{bot}/rules", MailStaysInside());
        var stillThere = await Send<List<BotRuleResponse>>(asAdmin.Get($"{bot}/rules"), HttpStatusCode.OK);

        unsigned.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a state-changing request needs the antiforgery token");
        stillThere.Should().ContainSingle().Which.Should().Match<BotRuleResponse>(found => found.Enabled, "no refused request changed the rule; an admin reads rules");
    }

    #endregion

    #region Inheritance

    [Fact]
    public async Task A_bot_follows_the_rules_of_the_bots_above_it_and_can_add_its_own_but_never_change_theirs()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var parent = await NewBot(client, alice.SpaceId, "Atlas");
        var child = $"{Space(alice.SpaceId)}/bots/{await SeedChild(alice.SpaceId, alice.UserId, Guid.Parse(parent.Split('/')[^1]), "Scout")}";
        var theirs = await Send<BotRuleResponse>(client.Post($"{parent}/rules", new BotRuleBody("Never say the project name", true, "before_reply", "block", [new("text_contains") { Entries = ["Project Lantern"] }])), HttpStatusCode.Created);
        var own = await Send<BotRuleResponse>(client.Post($"{child}/rules", new BotRuleBody("Redact it too", true, "before_reply", "redact", [new("text_contains") { Entries = ["Project Lantern"] }])), HttpStatusCode.Created);

        var followed = await Send<List<BotRuleResponse>>(client.Get($"{child}/rules"), HttpStatusCode.OK);
        var changeTheirs = await client.Put($"{child}/rules/{theirs.Id}", new BotRuleBody("Loosened", false, "before_reply", "redact", [new("text_contains") { Entries = ["x"] }]));
        var removeTheirs = await client.Delete($"{child}/rules/{theirs.Id}");
        var tried = await Send<TryRulesResponse>(client.Post($"{child}/rules/try", new TryRulesRequest("before_reply", null, null, "About Project Lantern.")), HttpStatusCode.OK);
        var parentOnly = await Send<List<BotRuleResponse>>(client.Get($"{parent}/rules"), HttpStatusCode.OK);

        followed.Select(rule => (rule.Name, rule.Inherited, rule.BotName)).Should().Equal(
            ("Never say the project name", true, "Atlas"),
            ("Redact it too", false, "Scout"));
        changeTheirs.StatusCode.Should().Be(HttpStatusCode.NotFound, "a rule from a bot above cannot be changed through the bot beneath");
        removeTheirs.StatusCode.Should().Be(HttpStatusCode.NotFound);
        tried.Should().Match<TryRulesResponse>(found => found.Effect == "block" && found.RuleId == theirs.Id, "the child's gentler rule does not loosen its parent's");
        tried.Applied.Should().Equal("Never say the project name", "Redact it too");
        parentOnly.Should().ContainSingle().Which.Id.Should().Be(theirs.Id, "a rule goes down the tree, never up");
        own.Inherited.Should().BeFalse();
    }

    #endregion

    #region Try It

    [Fact]
    public async Task A_sample_too_long_to_try_is_refused_in_words_before_any_rule_is_run()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await NewBot(client, alice.SpaceId);
        await Send<BotRuleResponse>(client.Post($"{bot}/rules", new BotRuleBody("Slow pattern", true, "before_reply", "block", [new("text_matches") { Pattern = "a+b|a" }])), HttpStatusCode.Created);
        var watch = Stopwatch.StartNew();

        var refused = await client.Post($"{bot}/rules/try", new TryRulesRequest("before_reply", null, null, new string('a', RuleService.MaxSampleLength + 1)));

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync(Token)).Should().Contain("20,000 characters");
        var tried = await Send<TryRulesResponse>(client.Post($"{bot}/rules/try", new TryRulesRequest("before_reply", null, null, new string('a', RuleService.MaxSampleLength))), HttpStatusCode.OK);
        tried.Effect.Should().Be("block");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Trying_a_sample_answers_with_the_evaluator_a_run_uses_and_changes_nothing()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var bot = await NewBot(client, alice.SpaceId);
        var botId = Guid.Parse(bot.Split('/')[^1]);
        await Send<BotRuleResponse>(client.Post($"{bot}/rules", MailStaysInside()), HttpStatusCode.Created);
        var off = await Send<BotRuleResponse>(client.Post($"{bot}/rules", new BotRuleBody("Switched off", false, "before_tool", "stop", [])), HttpStatusCode.Created);
        var redact = await Send<BotRuleResponse>(client.Post($"{bot}/rules", new BotRuleBody("No cards", true, "before_reply", "redact", [new("text_holds") { Builtin = "card_number" }])), HttpStatusCode.Created);
        TryRulesRequest[] samples =
        [
            new("before_tool", "email.send", """{"to": ["a@example.org", "b@outside.test"]}""", null),
            new("before_tool", "email.send", """{"to": "a@example.org"}""", null),
            new("before_tool", "notes.write", """{"to": "b@outside.test"}""", null),
            new("before_reply", null, null, "Card 4111 1111 1111 1111 on file."),
            new("before_reply", null, null, "Nothing to see."),
            new("message_arrives", null, null, "Card 4111 1111 1111 1111 on file."),
        ];

        List<TryRulesResponse> answers = [];

        foreach (var sample in samples)
        {
            answers.Add(await Send<TryRulesResponse>(client.Post($"{bot}/rules/try", sample), HttpStatusCode.OK));
        }

        var active = await InSpace(alice.SpaceId, alice.UserId, async services =>
            RuleService.Active(await services.GetRequiredService<BotRuleRepository>().ListRulesAlongPath(botId, Token)));
        var direct = samples.Select(sample => RuleEvaluator.Evaluate(active, new RuleSubject
        {
            Checkpoint = sample.Checkpoint is "before_tool" ? RuleCheckpoint.BeforeTool : sample.Checkpoint is "before_reply" ? RuleCheckpoint.BeforeReply : RuleCheckpoint.MessageArrives,
            ToolName = sample.Tool,
            Json = sample.Json is null ? null : JsonElement.Parse(sample.Json),
            Text = sample.Tool is null ? sample.Text : null,
            Now = Time.GetUtcNow(),
        })).ToList();
        var draft = await Send<TryRulesResponse>(
            client.Post($"{bot}/rules/try", new TryRulesRequest("before_reply", null, null, "Nothing to see.", new BotRuleBody("Draft", true, "before_reply", "stop", [new("text_contains") { Entries = ["nothing"] }]))),
            HttpStatusCode.OK);
        var badDraft = await client.Post($"{bot}/rules/try", new TryRulesRequest("before_reply", null, null, "x", new BotRuleBody("Draft", true, "before_reply", "ask", [])));
        var badJson = await client.Post($"{bot}/rules/try", new TryRulesRequest("before_tool", "email.send", "{not json", null));

        answers.Select(answer => answer.Effect).Should().Equal("ask", null, null, "redact", null, null);
        answers.Select(answer => (answer.Effect, answer.Why, answer.RedactedText)).Should().Equal(
            direct.Select(outcome => (outcome?.Effect.ToString().ToLowerInvariant(), outcome?.Why, outcome?.RedactedText)),
            "the API and the evaluator called directly with the rules a run loads agree on every sample");
        answers[0].Why.Should().Be("the tool is email.send, and the argument to has a value that is not on the rule's list");
        answers[3].Should().Match<TryRulesResponse>(answer => answer.RuleId == redact.Id && answer.RedactedText == $"Card {RuleEvaluator.Redaction} on file.");
        active.Should().NotContain(rule => rule.Id == off.Id, "a rule that is switched off is not checked");
        draft.Should().Match<TryRulesResponse>(answer => answer.Effect == "stop" && answer.RuleName == "Draft" && answer.RuleId == null, "a draft is tried without being saved");
        badDraft.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a draft is held to the same checks as a saved rule");
        badJson.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Send<List<BotRuleResponse>>(client.Get($"{bot}/rules"), HttpStatusCode.OK)).Should().HaveCount(3, "trying stores nothing");
    }

    #endregion

    #region Audit

    [Fact]
    public async Task Rule_changes_are_in_the_audit_log_with_who_and_before_and_after()
    {
        var alice = await SeedPerson("alice");
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var asOwner = await SignIn(alice.Email, alice.Password);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var bot = await NewBot(asOwner, alice.SpaceId);
        var botId = Guid.Parse(bot.Split('/')[^1]);

        var rule = await Send<BotRuleResponse>(asOwner.Post($"{bot}/rules", MailStaysInside()), HttpStatusCode.Created);
        await Send<BotRuleResponse>(asAdmin.Put($"{bot}/rules/{rule.Id}", MailStaysInside() with { Effect = "block" }), HttpStatusCode.OK);
        await Send<BotRuleResponse>(asAdmin.Put($"{bot}/rules/{rule.Id}", MailStaysInside() with { Effect = "block" }), HttpStatusCode.OK);
        (await asOwner.Delete($"{bot}/rules/{rule.Id}")).EnsureSuccessStatusCode();
        var entries = (await InSpace(alice.SpaceId, alice.UserId, services => services.GetRequiredService<AuditRepository>().ListEntriesAbout(botId, Token)))
            .Where(entry => entry.Action.StartsWith("rule.", StringComparison.Ordinal)).ToList();

        entries.Select(entry => entry.Action).Should().Equal(["rule.added", "rule.changed", "rule.removed"], "saving the same rule again is not a change");
        entries.Should().OnlyContain(entry => entry.ActorKind == ActorKind.User && entry.TargetKind == "bot" && entry.Data.GetProperty("rule_id").GetGuid() == rule.Id);
        entries[1].ActorId.Should().Be(admin.UserId);
        var effect = entries[1].Data.GetProperty("changes").GetProperty("effect");
        (effect.GetProperty("before").GetString(), effect.GetProperty("after").GetString()).Should().Be(("ask", "block"));
        entries[1].Data.GetProperty("changes").EnumerateObject().Select(change => change.Name).Should().Equal("effect");
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static BotRuleBody MailStaysInside() =>
        new("Mail stays inside", true, "before_tool", "ask", [new("tool_is") { Tools = ["email.send"] }, new("not_in_list") { Argument = "to", Entries = ["example.org"] }]);

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    private static async Task<T> Send<T>(Task<HttpResponseMessage> request, HttpStatusCode expected)
    {
        var response = await request;
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>A new bot on a new connection. Returns the bot's address under the API.</summary>
    private static async Task<string> NewBot(ApiClient client, Guid spaceId, string name = "Helper")
    {
        var connection = await Send<ModelConnectionSummary>(
            client.Post($"{Space(spaceId)}/model-connections", new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, $"For {name}", null, null, "scripted")),
            HttpStatusCode.Created);
        var bot = await Send<BotSummary>(client.Post($"{Space(spaceId)}/bots", new CreateBotRequest(name, "Answer briefly.", connection.Id, null)), HttpStatusCode.Created);
        return $"{Space(spaceId)}/bots/{bot.Id}";
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

    private Task<Guid> SeedChild(Guid spaceId, Guid userId, Guid parentId, string name) =>
        InSpace(spaceId, userId, async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            var parent = await db.Bots.AsNoTracking().SingleAsync(bot => bot.Id == parentId, Token);
            var child = new Bot { Name = name, Handle = name.ToLowerInvariant(), ParentBotId = parentId, Depth = parent.Depth + 1, ModelConnectionId = parent.ModelConnectionId, Model = parent.Model, CreatedByBotId = parentId };
            db.Bots.Add(child);
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = child.Id, DescendantBotId = child.Id, Distance = 0 });
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = parentId, DescendantBotId = child.Id, Distance = 1 });
            await db.SaveChangesAsync(Token);
            return child.Id;
        });

    #endregion
}
