namespace Drover.Server.Tests;

/// <summary>Projects over the API, including roles, conversation ownership, isolation, removal, and audit facts.</summary>
public sealed class ProjectApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Private Fields

    private ScriptedModelProvider? model;

    #endregion

    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => model ??= new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region Lifecycle

    [Fact]
    public async Task A_project_is_created_changed_lists_its_visible_conversations_and_is_removed_without_the_conversation()
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);
        var conversation = await NewConversation(client, alice.SpaceId);
        var projects = Projects(alice.SpaceId);
        var secretInstructions = "Keep the release details inside this project.";

        var created = await Send<ProjectResponse>(
            client.Post(projects, new ProjectBody("Release", secretInstructions)),
            HttpStatusCode.Created);
        var placed = await client.Put(
            $"{Space(alice.SpaceId)}/threads/{conversation.Id}/project",
            new SetConversationProjectRequest(created.Id));
        placed.StatusCode.Should().Be(HttpStatusCode.NoContent, await placed.Content.ReadAsStringAsync(Token));
        var changed = await Send<ProjectResponse>(
            client.Put($"{projects}/{created.Id}", new ProjectBody("Launch", "Use the approved launch notes.")),
            HttpStatusCode.OK);
        var listed = await Send<List<ProjectResponse>>(client.Get(projects), HttpStatusCode.OK);
        var before = await Conversation(alice.SpaceId, alice.UserId, conversation.Id);

        (await client.Delete($"{projects}/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await Conversation(alice.SpaceId, alice.UserId, conversation.Id);
        changed.Should().Match<ProjectResponse>(project => project.Id == created.Id && project.Name == "Launch");
        listed.Should().ContainSingle().Which.ConversationIds.Should().Equal(conversation.Id);
        after.ProjectId.Should().BeNull();
        after.Should().BeEquivalentTo(before, options => options.Excluding(thread => thread.ProjectId));
        (await Send<List<ProjectResponse>>(client.Get(projects), HttpStatusCode.OK)).Should().BeEmpty();

        var audit = (await Audit(alice.SpaceId, alice.UserId))
            .Where(entry => entry.TargetKind == AuditTargets.Project)
            .ToList();
        audit.Select(entry => entry.Action).Should().Equal(
            "project.created",
            "conversation.project_changed",
            "project.updated",
            "project.removed");
        JsonSerializer.Serialize(audit.Select(entry => entry.Data)).Should().NotContain(secretInstructions);
        audit.Single(entry => entry.Action == "project.updated").Data.GetProperty("fields")
            .EnumerateArray().Select(field => field.GetString()).Should().Equal("name", "instructions");
    }

    [Theory]
    [MemberData(nameof(InvalidProjects))]
    public async Task Invalid_project_definitions_are_refused(string? name, string? instructions, string expected)
    {
        var alice = await SeedPerson("alice");
        var client = await SignIn(alice.Email, alice.Password);

        var response = await client.Post(Projects(alice.SpaceId), new ProjectBody(name, instructions));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Title(response)).Should().Contain(expected);
        (await Send<List<ProjectResponse>>(client.Get(Projects(alice.SpaceId)), HttpStatusCode.OK)).Should().BeEmpty();
    }

    public static TheoryData<string?, string?, string> InvalidProjects() => new()
    {
        { " ", string.Empty, "Give the project a name" },
        { new string('n', Project.MaxNameLength + 1), string.Empty, "Give the project a name" },
        { "Sample", new string('i', Project.MaxInstructionsLength + 1), "Keep the project instructions" },
    };

    #endregion

    #region Access

    [Fact]
    public async Task Members_read_shared_context_but_only_admins_change_it_and_only_a_conversation_owner_moves_their_conversation()
    {
        var alice = await SeedPerson("alice");
        var outsider = await SeedPerson("outsider");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var admin = await SeedMember(alice.SpaceId, "admin", MembershipRole.Admin);
        var asOwner = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var asOutsider = await SignIn(outsider.Email, outsider.Password);
        var project = await Send<ProjectResponse>(
            asOwner.Post(Projects(alice.SpaceId), new ProjectBody("Shared", "Use the shared facts.")),
            HttpStatusCode.Created);
        var conversation = await NewConversation(asOwner, alice.SpaceId);
        await ShareConversation(alice.SpaceId, alice.UserId, conversation.Id);

        var visible = await Send<List<ProjectResponse>>(asMember.Get(Projects(alice.SpaceId)), HttpStatusCode.OK);
        var memberCreate = await asMember.Post(Projects(alice.SpaceId), new ProjectBody("No", string.Empty));
        var memberUpdate = await asMember.Put($"{Projects(alice.SpaceId)}/{project.Id}", new ProjectBody("No", string.Empty));
        var memberRemove = await asMember.Delete($"{Projects(alice.SpaceId)}/{project.Id}");
        var memberMove = await asMember.Put(
            $"{Space(alice.SpaceId)}/threads/{conversation.Id}/project",
            new SetConversationProjectRequest(project.Id));
        var adminUpdate = await asAdmin.Put(
            $"{Projects(alice.SpaceId)}/{project.Id}",
            new ProjectBody("Shared facts", "Use the shared facts."));
        var outsiderList = await asOutsider.Get(Projects(alice.SpaceId));

        visible.Should().ContainSingle().Which.Should().Match<ProjectResponse>(found =>
            found.Id == project.Id && found.Instructions == "Use the shared facts.");
        memberCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        memberUpdate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        memberRemove.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        memberMove.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        adminUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        outsiderList.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_conversation_cannot_join_a_project_from_another_space_and_private_membership_stays_private()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice.Email, alice.Password);
        var asBob = await SignIn(bob.Email, bob.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var aliceProject = await Send<ProjectResponse>(
            asAlice.Post(Projects(alice.SpaceId), new ProjectBody("Alice", string.Empty)),
            HttpStatusCode.Created);
        var bobProject = await Send<ProjectResponse>(
            asBob.Post(Projects(bob.SpaceId), new ProjectBody("Bob", string.Empty)),
            HttpStatusCode.Created);
        var conversation = await NewConversation(asAlice, alice.SpaceId);

        var crossSpace = await asAlice.Put(
            $"{Space(alice.SpaceId)}/threads/{conversation.Id}/project",
            new SetConversationProjectRequest(bobProject.Id));
        var placed = await asAlice.Put(
            $"{Space(alice.SpaceId)}/threads/{conversation.Id}/project",
            new SetConversationProjectRequest(aliceProject.Id));
        var memberList = await Send<List<ProjectResponse>>(asMember.Get(Projects(alice.SpaceId)), HttpStatusCode.OK);

        crossSpace.StatusCode.Should().Be(HttpStatusCode.NotFound);
        placed.StatusCode.Should().Be(HttpStatusCode.NoContent, await placed.Content.ReadAsStringAsync(Token));
        memberList.Single().ConversationIds.Should().BeEmpty("a project never widens a private conversation's audience");
    }

    [Fact]
    public async Task Removing_a_project_clears_a_hidden_private_membership_without_auditing_the_conversation_id()
    {
        var alice = await SeedPerson("alice");
        var member = await SeedMember(alice.SpaceId, "member", MembershipRole.Member);
        var asAlice = await SignIn(alice.Email, alice.Password);
        var asMember = await SignIn(member.Email, member.Password);
        var project = await Send<ProjectResponse>(
            asAlice.Post(Projects(alice.SpaceId), new ProjectBody("Shared", string.Empty)),
            HttpStatusCode.Created);
        var bot = await NewBot(asAlice, alice.SpaceId);
        var conversation = await NewConversation(asMember, alice.SpaceId, bot.Id);

        var placed = await asMember.Put(
            $"{Space(alice.SpaceId)}/threads/{conversation.Id}/project",
            new SetConversationProjectRequest(project.Id));
        var visibleToAlice = await Send<List<ProjectResponse>>(
            asAlice.Get(Projects(alice.SpaceId)),
            HttpStatusCode.OK);
        var removed = await asAlice.Delete($"{Projects(alice.SpaceId)}/{project.Id}");

        placed.StatusCode.Should().Be(HttpStatusCode.NoContent, await placed.Content.ReadAsStringAsync(Token));
        visibleToAlice.Single().ConversationIds.Should().BeEmpty();
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent, await removed.Content.ReadAsStringAsync(Token));
        (await Conversation(alice.SpaceId, member.UserId, conversation.Id)).ProjectId.Should().BeNull();

        var projectAudit = (await Audit(alice.SpaceId, alice.UserId))
            .Where(entry => entry.Action.StartsWith("project.", StringComparison.Ordinal)
                || entry.Action == "conversation.project_changed")
            .ToList();
        JsonSerializer.Serialize(projectAudit).Should().NotContain(conversation.Id.ToString());
    }

    #endregion

    #region Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private static string Projects(Guid spaceId) => $"{Space(spaceId)}/projects";

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<ThreadSummary> NewConversation(ApiClient client, Guid spaceId)
    {
        var bot = await NewBot(client, spaceId);
        return await NewConversation(client, spaceId, bot.Id);
    }

    private static async Task<BotSummary> NewBot(ApiClient client, Guid spaceId)
    {
        var connection = await Send<ModelConnectionSummary>(
            client.Post(
                $"{Space(spaceId)}/model-connections",
                new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, "For projects", null, null, "scripted")),
            HttpStatusCode.Created);
        return await Send<BotSummary>(
            client.Post($"{Space(spaceId)}/bots", new CreateBotRequest("Helper", string.Empty, connection.Id, null)),
            HttpStatusCode.Created);
    }

    private static Task<ThreadSummary> NewConversation(ApiClient client, Guid spaceId, Guid botId) =>
        Send<ThreadSummary>(
            client.Post($"{Space(spaceId)}/bots/{botId}/threads", new CreateThreadRequest("Project conversation")),
            HttpStatusCode.Created);

    private Task<ChatThread> Conversation(Guid spaceId, Guid userId, Guid conversationId) =>
        InSpace(spaceId, userId, db => db.Threads.AsNoTracking().SingleAsync(thread => thread.Id == conversationId, Token));

    private Task<List<AuditLogEntry>> Audit(Guid spaceId, Guid userId) =>
        InSpace(
            spaceId,
            userId,
            db => db.AuditLog.AsNoTracking().OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Id).ToListAsync(Token));

    private Task<bool> ShareConversation(Guid spaceId, Guid userId, Guid conversationId) =>
        InSpace(
            spaceId,
            userId,
            async db =>
            {
                return await db.Threads.Where(conversation => conversation.Id == conversationId)
                    .ExecuteUpdateAsync(
                        set => set.SetProperty(conversation => conversation.AudienceUserId, Audience.Everyone),
                        Token) == 1;
            });

    private async Task<T> InSpace<T>(Guid spaceId, Guid userId, Func<DroverDbContext, Task<T>> work)
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        provider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var tenant = await provider.GetRequiredService<TenantScope>().Begin(Token);
        var result = await work(provider.GetRequiredService<DroverDbContext>());
        await tenant.Commit(Token);
        return result;
    }

    private static async Task<T> Send<T>(Task<HttpResponseMessage> request, HttpStatusCode expected)
    {
        var response = await request;
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<string?> Title(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Token)).GetProperty("title").GetString();

    #endregion
}
