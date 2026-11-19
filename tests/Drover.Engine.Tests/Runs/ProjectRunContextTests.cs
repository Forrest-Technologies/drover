namespace Drover.Engine.Tests.Runs;

/// <summary>
/// A run in a project receives that project's instructions as a labeled source beside the bot's, and its files only
/// through the attachment grant path. A run outside a project is unchanged. Grants stay with the bot. A private
/// conversation stays private.
/// </summary>
public sealed class ProjectRunContextTests(PostgresServer postgres)
{
    #region Private Fields

    private const string BotInstructions = "Answer briefly.";

    #endregion

    #region Instructions And Files

    [Fact]
    public async Task A_run_outside_a_project_keeps_the_bots_instructions()
    {
        var script = new ScriptedModel([new TextOutput("Done.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost();
        var thread = await world.NewThread();

        var runId = await world.Post(thread, "Hello.");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var request = world.Model.Requests.Should().ContainSingle().Subject;
        request.Options.Instructions.Should().Be(BotInstructions);
        TextOf(request).Should().NotContain(ProjectInstructionSource.Heading);
        TextOf(request).Should().NotContain(ProjectFileGrant.Heading);
    }

    [Fact]
    public async Task Empty_project_instructions_add_no_source_and_a_project_file_is_not_an_instruction()
    {
        var script = new ScriptedModel([new TextOutput("Done.")], [new TextOutput("Noted.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost();
        var project = await CreateProject(world, "Notes", instructions: "");
        var foreign = await CreateProject(world, "Other");
        await Upload(world, foreign.Id, "secret.txt", "foreign project secret");
        var thread = await ThreadIn(world, world.BotId, project.Id);
        thread.Should().NotBeNull();

        var first = await world.Post(thread!.Id, "Anything shared?");
        (await world.WaitForStatus(first, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var beforeFile = world.Model.Requests.Should().ContainSingle().Subject;
        beforeFile.Options.Instructions.Should().Be(BotInstructions, "empty project instructions add no labeled source");
        TextOf(beforeFile).Should().NotContain(ProjectFileGrant.Heading);
        (await world.ReadRun(first)).Tainted.Should().BeFalse("instructions alone are not a file");

        var uploaded = await Upload(world, project.Id, "note.txt", "quarterly figures stay in the file");
        await Upload(world, project.Id, "diagram.png", TestPng.Make(4, 4));
        var second = await world.Post(thread.Id, "And the files?");
        (await world.WaitForStatus(second, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var withFiles = world.Model.Requests[1];
        withFiles.Options.Instructions.Should().Be(BotInstructions);
        var body = TextOf(withFiles);
        body.Should().Contain(ProjectFileGrant.Heading);
        body.Should().Contain("quarterly figures stay in the file");
        body.Should().Contain("cannot read pictures");
        body.Should().NotContain("foreign project secret");
        withFiles.Options.Instructions.Should().NotContain("quarterly figures");
        ToolNames(withFiles).Should().Equal(ToolNames(beforeFile), "a project file adds no tool");
        (await world.ReadRun(second)).Tainted.Should().BeTrue("a project file is untrusted input, as an attachment is");
        RunWorld.Summaries(await world.ReadMessages(thread.Id)).Should().NotContain(text => text.Contains("quarterly figures", StringComparison.Ordinal), "the grant is rebuilt for each call and not stored");
        uploaded.File.Should().NotBeNull(uploaded.Detail);
    }

    [Fact]
    public async Task A_project_run_receives_labeled_instructions_and_the_next_execution_reads_them_again()
    {
        var script = new ScriptedModel([new TextOutput("One.")], [new TextOutput("Two.")], [new TextOutput("Three.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.StartHost();
        var project = await CreateProject(world, "Weekly", "Ship the weekly note.");
        var file = await Upload(world, project.Id, "brief.txt", "alpha contents");
        var thread = await ThreadIn(world, world.BotId, project.Id);
        thread.Should().NotBeNull();

        var first = await world.Post(thread!.Id, "Start.");
        (await world.WaitForStatus(first, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var opened = world.Model.Requests[0];
        opened.Options.Instructions.Should().StartWith(BotInstructions);
        opened.Options.Instructions.Should().Contain(ProjectInstructionSource.Heading);
        opened.Options.Instructions.Should().Contain("Ship the weekly note.");
        opened.Options.Instructions.Should().NotContain("alpha contents");
        TextOf(opened).Should().Contain(ProjectFileGrant.Heading).And.Contain("alpha contents");
        RunWorld.Summaries(await world.ReadMessages(thread.Id)).Should().NotContain(text => text.Contains("Ship the weekly note.", StringComparison.Ordinal));

        await world.Transact(services => services.GetRequiredService<ProjectService>().Update(project.Id, null, "Version two of the note.", AuditActor.Person(world.UserId), RunWorld.Token));
        await world.Read(async db =>
        {
            (await db.Bots.SingleAsync(bot => bot.Id == world.BotId, RunWorld.Token)).Instructions = "Answer in one line.";
            return true;
        });
        (await RemoveFile(world, project.Id, file.File!.Id)).Should().BeTrue();

        var second = await world.Post(thread.Id, "Again.");
        (await world.WaitForStatus(second, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var reread = world.Model.Requests[1];
        reread.Options.Instructions.Should().StartWith("Answer in one line.");
        reread.Options.Instructions.Should().Contain("Version two of the note.");
        reread.Options.Instructions.Should().NotContain("Ship the weekly note.");
        TextOf(reread).Should().NotContain("alpha contents");
        TextOf(reread).Should().NotContain(ProjectFileGrant.Heading);

        (await RemoveProject(world, project.Id)).Should().BeTrue();
        var third = await world.Post(thread.Id, "After.");
        (await world.WaitForStatus(third, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        world.Model.Requests[2].Options.Instructions.Should().Be("Answer in one line.");
        TextOf(world.Model.Requests[2]).Should().NotContain(ProjectInstructionSource.Heading);
    }

    [Fact]
    public async Task A_retry_reads_the_project_again_the_way_it_reads_the_bot()
    {
        var script = new ScriptedModel([new TextOutput("Retried.")]);
        await using var world = await RunWorld.Create(postgres, script);
        world.Model.FailNext = ModelProviderException.FromStatus(402);
        world.StartHost();
        var project = await CreateProject(world, "Weekly", "Ship the weekly note.");
        var file = await Upload(world, project.Id, "brief.txt", "alpha contents");
        var thread = await ThreadIn(world, world.BotId, project.Id);
        thread.Should().NotBeNull();

        var failedId = await world.Post(thread!.Id, "Start.");
        (await world.WaitForStatus(failedId, RunStatus.Failed)).Status.Should().Be(RunStatus.Failed);
        var failed = world.Model.Requests.Should().ContainSingle().Subject;
        failed.Options.Instructions.Should().Contain("Ship the weekly note.");
        TextOf(failed).Should().Contain("alpha contents");

        await world.Transact(services => services.GetRequiredService<ProjectService>().Update(project.Id, null, "Version two of the note.", AuditActor.Person(world.UserId), RunWorld.Token));
        await world.Read(async db =>
        {
            (await db.Bots.SingleAsync(bot => bot.Id == world.BotId, RunWorld.Token)).Instructions = "Answer in one line.";
            return true;
        });
        (await RemoveFile(world, project.Id, file.File!.Id)).Should().BeTrue();

        var retry = await world.WithService<RunService, RunRetry>(runs => runs.Retry(failedId, world.UserId, RunWorld.Token));
        retry.Outcome.Should().Be(RetryOutcome.Queued);
        (await world.WaitForStatus(retry.Run!.Id, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var again = world.Model.Requests[1];
        again.Options.Instructions.Should().StartWith("Answer in one line.");
        again.Options.Instructions.Should().Contain("Version two of the note.");
        again.Options.Instructions.Should().NotContain("Ship the weekly note.");
        again.Options.Instructions.Should().NotContain("alpha contents");
        TextOf(again).Should().NotContain("alpha contents");
        TextOf(again).Should().NotContain(ProjectFileGrant.Heading);
    }

    [Fact]
    public async Task Two_bots_in_one_project_do_not_share_grants_and_private_conversations_stay_private()
    {
        var script = new ScriptedModel([new TextOutput("Owner.")], [new TextOutput("Other.")], [new TextOutput("Member.")]);
        await using var world = await RunWorld.Create(postgres, script);
        await TurnMemoryOn(world, world.BotId);
        await world.Grant("memory.list", GrantEffect.Allow);
        var otherBot = await world.Transact(async services =>
        {
            var change = await services.GetRequiredService<BotService>().Create(
                new NewBot("Other", string.Empty, "Stay in your lane.", world.ConnectionId, null, null),
                AuditActor.Person(world.UserId),
                RunWorld.Token);
            change.Problem.Should().BeNull();
            return change.Bot!.Id;
        });
        await TurnMemoryOn(world, otherBot);
        await world.GrantTo(otherBot, "memory.save", GrantEffect.Allow);
        world.StartHost();
        var project = await CreateProject(world, "Crew", "Shared brief for both bots.");
        var ownerThread = await ThreadIn(world, world.BotId, project.Id);
        var otherThread = await ThreadIn(world, otherBot, project.Id);
        ownerThread.Should().NotBeNull();
        otherThread.Should().NotBeNull();
        var member = await world.AddMember(MembershipRole.Member);
        var memberThread = await world.TransactAs(world.SpaceId, member, services =>
            services.GetRequiredService<ThreadService>().Create(world.BotId, "Member", member, RunWorld.Token, project.Id));
        memberThread.Should().NotBeNull();

        var ownerRun = await world.Post(ownerThread!.Id, "owner-only-phrase");
        (await world.WaitForStatus(ownerRun, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var otherRun = await world.Post(otherThread!.Id, "other bot speaking");
        (await world.WaitForStatus(otherRun, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var memberPost = await world.PostRaw(memberThread!.Id, "member-only-phrase", asUser: member);
        memberPost.Problem.Should().Be(PostProblem.None, memberPost.Detail);
        (await world.WaitForStatus(memberPost.Run!.Id, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var ownerRequest = world.Model.Requests[0];
        var otherRequest = world.Model.Requests[1];
        var memberRequest = world.Model.Requests[2];
        ownerRequest.Options.Instructions.Should().Contain("Shared brief for both bots.").And.Contain(BotInstructions);
        otherRequest.Options.Instructions.Should().Contain("Shared brief for both bots.").And.Contain("Stay in your lane.");
        memberRequest.Options.Instructions.Should().Contain("Shared brief for both bots.");
        ToolNames(ownerRequest).Should().Contain("memory_list").And.NotContain("memory_save");
        ToolNames(otherRequest).Should().Contain("memory_save").And.NotContain("memory_list");
        ToolNames(ownerRequest).Concat(ToolNames(otherRequest)).Should().NotContain(name => name.StartsWith("sandbox_", StringComparison.Ordinal));
        TextOf(ownerRequest).Should().NotContain("member-only-phrase");
        TextOf(memberRequest).Should().NotContain("owner-only-phrase");
        TextOf(otherRequest).Should().NotContain("owner-only-phrase").And.NotContain("member-only-phrase");
    }

    #endregion

    #region Private Helpers

    private static Task<bool> TurnMemoryOn(RunWorld world, Guid botId) =>
        world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == botId, RunWorld.Token);
            bot.MemorySettings = bot.MemorySettings with { Enabled = true };
            return true;
        });

    private static Task<Project> CreateProject(RunWorld world, string name, string instructions = "") =>
        world.Transact(async services =>
        {
            var result = await services.GetRequiredService<ProjectService>()
                .Create(name, instructions, AuditActor.Person(world.UserId), RunWorld.Token);
            result.Project.Should().NotBeNull(result.Problem);
            return result.Project!;
        });

    private static Task<ChatThread?> ThreadIn(RunWorld world, Guid botId, Guid projectId) =>
        world.Transact(services => services.GetRequiredService<ThreadService>().Create(botId, "Work", world.UserId, RunWorld.Token, projectId));

    private static async Task<FileUpload> Upload(RunWorld world, Guid projectId, string name, string text) =>
        await Upload(world, projectId, name, Encoding.UTF8.GetBytes(text));

    private static async Task<FileUpload> Upload(RunWorld world, Guid projectId, string name, byte[] bytes)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        return await scope.ServiceProvider.GetRequiredService<FileService>()
            .UploadForProject(projectId, name, new MemoryStream(bytes), bytes.Length, world.UserId, RunWorld.Token);
    }

    private static async Task<bool> RemoveFile(RunWorld world, Guid projectId, Guid fileId)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        return await scope.ServiceProvider.GetRequiredService<FileService>().RemoveProjectFile(projectId, fileId, RunWorld.Token);
    }

    private static async Task<bool> RemoveProject(RunWorld world, Guid projectId)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, world.UserId);
        return await scope.ServiceProvider.GetRequiredService<ProjectService>().Remove(projectId, AuditActor.Person(world.UserId), RunWorld.Token);
    }

    private static IReadOnlyList<string> ToolNames(ModelChatRequest request) =>
        [.. (request.Options.Tools ?? []).Select(tool => tool.Name)];

    private static string Text(ModelChatMessage message) =>
        string.Join("\n", message.Contents.Select(content => content switch
        {
            ModelText text => text.Text,
            _ => string.Empty,
        }));

    private static string TextOf(ModelChatRequest request) => string.Join("\n", request.Messages.Select(Text));

    #endregion
}
