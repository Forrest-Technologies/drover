namespace Drover.Server.Tests;

/// <summary>
/// Projects and conversation membership through the HTTP API over real PostgreSQL: role boundaries, private vs shared
/// conversations, missing and cross-space ids, preservation on removal, and content-free audit entries.
/// </summary>
public sealed class ProjectApiTests(PostgresServer postgres) : ServerTestBase(postgres)
{
    #region Overrides

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddSingleton<IModelClientProvider>(_ => new ScriptedModelProvider(new ScriptedModel()));

    #endregion

    #region CRUD And Roles

    [Fact]
    public async Task Owners_and_admins_manage_projects_while_members_can_read_only()
    {
        var owner = await SeedPerson("owner");
        var admin = await SeedMember(owner.SpaceId, "admin", MembershipRole.Admin);
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Member);
        var asOwner = await SignIn(owner);
        var asAdmin = await SignIn(admin.Email, admin.Password);
        var asMember = await SignIn(member.Email, member.Password);

        var created = await PostFor<ProjectSummary>(
            asOwner,
            $"{Space(owner.SpaceId)}/projects",
            new CreateProjectRequest("  Q4 Launch  ", "Keep answers concise."),
            HttpStatusCode.Created);

        created.Name.Should().Be("Q4 Launch");
        created.Instructions.Should().Be("Keep answers concise.");

        var memberCreate = await asMember.Post($"{Space(owner.SpaceId)}/projects", new CreateProjectRequest("Denied"));
        var memberUpdate = await asMember.Put($"{Space(owner.SpaceId)}/projects/{created.Id}", new UpdateProjectRequest("Denied"));
        var memberDelete = await asMember.Delete($"{Space(owner.SpaceId)}/projects/{created.Id}");

        memberCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        memberUpdate.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        memberDelete.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var memberList = await GetFor<List<ProjectSummary>>(asMember, $"{Space(owner.SpaceId)}/projects");
        var memberRead = await GetFor<ProjectSummary>(asMember, $"{Space(owner.SpaceId)}/projects/{created.Id}");
        memberList.Should().ContainSingle().Which.Should().BeEquivalentTo(
            created,
            options => options
                .Using<DateTimeOffset>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromMilliseconds(1)))
                .WhenTypeIs<DateTimeOffset>());
        memberRead.Should().BeEquivalentTo(
            created,
            options => options
                .Using<DateTimeOffset>(ctx => ctx.Subject.Should().BeCloseTo(ctx.Expectation, TimeSpan.FromMilliseconds(1)))
                .WhenTypeIs<DateTimeOffset>());

        var renamed = await PutFor<ProjectSummary>(
            asAdmin,
            $"{Space(owner.SpaceId)}/projects/{created.Id}",
            new UpdateProjectRequest(Name: "  Launch Plan  "));
        renamed.Name.Should().Be("Launch Plan");
        renamed.Instructions.Should().Be("Keep answers concise.");

        var editedInstructions = await PutFor<ProjectSummary>(
            asAdmin,
            $"{Space(owner.SpaceId)}/projects/{created.Id}",
            new UpdateProjectRequest(Instructions: "Use bullet points."));
        editedInstructions.Name.Should().Be("Launch Plan");
        editedInstructions.Instructions.Should().Be("Use bullet points.");

        var deleted = await asAdmin.Delete($"{Space(owner.SpaceId)}/projects/{created.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRemove = await asOwner.Get($"{Space(owner.SpaceId)}/projects/{created.Id}");
        afterRemove.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Blank_names_and_overlong_names_or_instructions_are_rejected()
    {
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner);
        var project = await PostFor<ProjectSummary>(client, $"{Space(owner.SpaceId)}/projects", new CreateProjectRequest("Valid"), HttpStatusCode.Created);

        var blankName = await client.Post($"{Space(owner.SpaceId)}/projects", new CreateProjectRequest("   "));
        var longName = await client.Post($"{Space(owner.SpaceId)}/projects", new CreateProjectRequest(new string('a', Project.MaxNameLength + 1)));
        var longInstructions = await client.Post($"{Space(owner.SpaceId)}/projects", new CreateProjectRequest("Ok", new string('b', Project.MaxInstructionsLength + 1)));
        var updateBlank = await client.Put($"{Space(owner.SpaceId)}/projects/{project.Id}", new UpdateProjectRequest(Name: " "));
        var updateLongInstructions = await client.Put($"{Space(owner.SpaceId)}/projects/{project.Id}", new UpdateProjectRequest(Instructions: new string('c', Project.MaxInstructionsLength + 1)));

        blankName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        longName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        longInstructions.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        updateBlank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        updateLongInstructions.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Conversation Membership

    [Fact]
    public async Task A_conversation_owner_moves_conversations_between_projects_without_changing_audience_bot_or_model()
    {
        var owner = await SeedPerson("owner");
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Member);
        var asOwner = await SignIn(owner);
        var asMember = await SignIn(member.Email, member.Password);
        var botA = await CreateBot(asOwner, owner.SpaceId, "Alpha Bot");
        var botB = await CreateBot(asOwner, owner.SpaceId, "Beta Bot");
        var project = await PostFor<ProjectSummary>(asOwner, $"{Space(owner.SpaceId)}/projects", new CreateProjectRequest("Docs"), HttpStatusCode.Created);

        // Existing conversations with no project behave unchanged.
        var ungrouped = await PostFor<ThreadSummary>(asOwner, $"{Space(owner.SpaceId)}/bots/{botA.Id}/threads", new CreateThreadRequest("Ungrouped"), HttpStatusCode.Created);
        ungrouped.ProjectId.Should().BeNull();

        // Owner starts a private conversation directly in the project and moves an existing one into it.
        var ownerInProject = await PostFor<ThreadSummary>(asOwner, $"{Space(owner.SpaceId)}/bots/{botA.Id}/threads", new CreateThreadRequest("Owner private", project.Id), HttpStatusCode.Created);
        var memberPrivate = await PostFor<ThreadSummary>(asMember, $"{Space(owner.SpaceId)}/bots/{botB.Id}/threads", new CreateThreadRequest("Member private"), HttpStatusCode.Created);
        var memberMoved = await PutFor<ThreadSummary>(asMember, $"{Space(owner.SpaceId)}/threads/{memberPrivate.Id}/project", new SetThreadProjectRequest(project.Id));

        ownerInProject.ProjectId.Should().Be(project.Id);
        memberMoved.ProjectId.Should().Be(project.Id);
        memberMoved.BotId.Should().Be(botB.Id);
        memberMoved.IsShared.Should().BeFalse("joining a project does not widen audience");

        // Owner sees only their own conversation in the project; member sees only their own.
        var ownerProjectThreads = await GetFor<List<ThreadSummary>>(asOwner, $"{Space(owner.SpaceId)}/projects/{project.Id}/threads");
        var memberProjectThreads = await GetFor<List<ThreadSummary>>(asMember, $"{Space(owner.SpaceId)}/projects/{project.Id}/threads");
        ownerProjectThreads.Select(t => t.Id).Should().Equal([ownerInProject.Id]);
        memberProjectThreads.Select(t => t.Id).Should().Equal([memberPrivate.Id]);

        // Another member (even a space owner) cannot move another person's conversation, private or shared.
        var ownerMovesMemberPrivate = await asOwner.Put($"{Space(owner.SpaceId)}/threads/{memberPrivate.Id}/project", new SetThreadProjectRequest(null));
        ownerMovesMemberPrivate.StatusCode.Should().Be(HttpStatusCode.NotFound, "private conversation stays invisible");

        await ShareThread(owner.SpaceId, member.UserId, memberPrivate.Id);
        var ownerMovesMemberShared = await asOwner.Put($"{Space(owner.SpaceId)}/threads/{memberPrivate.Id}/project", new SetThreadProjectRequest(null));
        ownerMovesMemberShared.StatusCode.Should().Be(HttpStatusCode.Forbidden, "only the conversation owner may change project membership");

        var memberLeaves = await PutFor<ThreadSummary>(asMember, $"{Space(owner.SpaceId)}/threads/{memberPrivate.Id}/project", new SetThreadProjectRequest(null));
        memberLeaves.ProjectId.Should().BeNull();
        memberLeaves.IsShared.Should().BeTrue();
        memberLeaves.BotId.Should().Be(botB.Id);
    }

    [Fact]
    public async Task Removing_a_project_preserves_every_conversation_and_clears_their_project_id()
    {
        var owner = await SeedPerson("owner");
        var member = await SeedMember(owner.SpaceId, "member", MembershipRole.Member);
        var asOwner = await SignIn(owner);
        var asMember = await SignIn(member.Email, member.Password);
        var bot = await CreateBot(asOwner, owner.SpaceId);
        var project = await PostFor<ProjectSummary>(asOwner, $"{Space(owner.SpaceId)}/projects", new CreateProjectRequest("Cleanup"), HttpStatusCode.Created);

        var ownerThread = await PostFor<ThreadSummary>(asOwner, $"{Space(owner.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest("Owners", project.Id), HttpStatusCode.Created);
        var memberThread = await PostFor<ThreadSummary>(asMember, $"{Space(owner.SpaceId)}/bots/{bot.Id}/threads", new CreateThreadRequest("Members", project.Id), HttpStatusCode.Created);

        var removed = await asOwner.Delete($"{Space(owner.SpaceId)}/projects/{project.Id}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var ownerAfter = await GetFor<ThreadSummary>(asOwner, $"{Space(owner.SpaceId)}/threads/{ownerThread.Id}");
        var memberAfter = await GetFor<ThreadSummary>(asMember, $"{Space(owner.SpaceId)}/threads/{memberThread.Id}");
        var missingList = await asOwner.Get($"{Space(owner.SpaceId)}/projects/{project.Id}/threads");

        ownerAfter.ProjectId.Should().BeNull();
        ownerAfter.Title.Should().Be("Owners");
        memberAfter.ProjectId.Should().BeNull();
        memberAfter.Title.Should().Be("Members");
        missingList.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Cross Space And Audit

    [Fact]
    public async Task Cross_space_and_missing_ids_are_not_found()
    {
        var alice = await SeedPerson("alice");
        var bob = await SeedPerson("bob");
        var asAlice = await SignIn(alice);
        var asBob = await SignIn(bob);
        var alicesBot = await CreateBot(asAlice, alice.SpaceId);
        var alicesProject = await PostFor<ProjectSummary>(asAlice, $"{Space(alice.SpaceId)}/projects", new CreateProjectRequest("Alice"), HttpStatusCode.Created);
        var alicesThread = await PostFor<ThreadSummary>(asAlice, $"{Space(alice.SpaceId)}/bots/{alicesBot.Id}/threads", new CreateThreadRequest("Chat"), HttpStatusCode.Created);
        var missingId = Guid.CreateVersion7();

        var bobsBot = await CreateBot(asBob, bob.SpaceId);
        var bobsThread = await PostFor<ThreadSummary>(asBob, $"{Space(bob.SpaceId)}/bots/{bobsBot.Id}/threads", new CreateThreadRequest("Bob"), HttpStatusCode.Created);

        (await asBob.Get($"{Space(alice.SpaceId)}/projects")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Get($"{Space(bob.SpaceId)}/projects/{alicesProject.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Get($"{Space(bob.SpaceId)}/projects/{missingId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Put($"{Space(bob.SpaceId)}/projects/{alicesProject.Id}", new UpdateProjectRequest("Steal"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Delete($"{Space(bob.SpaceId)}/projects/{alicesProject.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Post($"{Space(bob.SpaceId)}/bots/{bobsBot.Id}/threads", new CreateThreadRequest("X", alicesProject.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Put($"{Space(bob.SpaceId)}/threads/{bobsThread.Id}/project", new SetThreadProjectRequest(alicesProject.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await asBob.Put($"{Space(bob.SpaceId)}/threads/{alicesThread.Id}/project", new SetThreadProjectRequest(null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Project_changes_write_audit_entries_without_storing_instruction_text()
    {
        const string SecretInstruction = "CONFIDENTIAL_PROJECT_INSTRUCTION_TEXT_123";
        const string UpdatedInstruction = "UPDATED_SECRET_INSTRUCTION_TEXT_456";
        var owner = await SeedPerson("owner");
        var client = await SignIn(owner);

        var created = await PostFor<ProjectSummary>(
            client,
            $"{Space(owner.SpaceId)}/projects",
            new CreateProjectRequest("Audit Project", SecretInstruction),
            HttpStatusCode.Created);
        await PutFor<ProjectSummary>(
            client,
            $"{Space(owner.SpaceId)}/projects/{created.Id}",
            new UpdateProjectRequest(Instructions: UpdatedInstruction));
        (await client.Delete($"{Space(owner.SpaceId)}/projects/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var rows = await ReadAuditEntries(owner.SpaceId, owner.UserId, created.Id);

        rows.Select(row => row.Action).Should().Equal("project.created", "project.updated", "project.removed");
        rows.Should().OnlyContain(row => row.TargetKind == AuditTargets.Project);
        foreach (var row in rows)
        {
            var raw = row.Data.GetRawText();
            raw.Should().NotContain(SecretInstruction);
            raw.Should().NotContain(UpdatedInstruction);
            raw.Should().Contain("instructions_sha256");
        }
    }

    #endregion

    #region Private Helpers

    private static string Space(Guid spaceId) => $"/api/spaces/{spaceId}";

    private Task<ApiClient> SignIn((string Email, string Password, Guid UserId, Guid SpaceId) person) =>
        SignIn(person.Email, person.Password);

    private async Task<ApiClient> SignIn(string email, string password)
    {
        var client = NewClient();
        (await client.SignIn(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<BotSummary> CreateBot(ApiClient client, Guid spaceId, string name = "Helper")
    {
        var connection = await PostFor<ModelConnectionSummary>(
            client,
            $"{Space(spaceId)}/model-connections",
            new CreateModelConnectionRequest(ScriptedModelProvider.ConnectionKind, $"For {name}", null, null, "scripted"),
            HttpStatusCode.Created);
        return await PostFor<BotSummary>(
            client,
            $"{Space(spaceId)}/bots",
            new CreateBotRequest(name, "Answer briefly.", connection.Id, null),
            HttpStatusCode.Created);
    }

    private static async Task<T> PostFor<T>(ApiClient client, string path, object body, HttpStatusCode expected)
    {
        var response = await client.Post(path, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<T> PutFor<T>(ApiClient client, string path, object body)
    {
        var response = await client.Put(path, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private static async Task<T> GetFor<T>(ApiClient client, string path)
    {
        var response = await client.Get(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Token));
        return (await response.Content.ReadFromJsonAsync<T>(Token))!;
    }

    private async Task ShareThread(Guid spaceId, Guid ownerUserId, Guid threadId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, ownerUserId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        await db.Threads
            .Where(t => t.Id == threadId)
            .ExecuteUpdateAsync(set => set.SetProperty(t => t.AudienceUserId, Audience.Everyone), Token);
        await work.Commit(Token);
    }

    private async Task<List<AuditLogEntry>> ReadAuditEntries(Guid spaceId, Guid userId, Guid targetId)
    {
        await using var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(spaceId, userId);
        await using var work = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(Token);
        var db = scope.ServiceProvider.GetRequiredService<DroverDbContext>();
        return await db.AuditLog
            .Where(entry => entry.TargetId == targetId)
            .OrderBy(entry => entry.Id)
            .ToListAsync(Token);
    }

    #endregion
}
