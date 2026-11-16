namespace Drover.Engine.Tests.Files;

/// <summary>
/// Files a bot shares from its sandbox: where the file goes, who may fetch it, the limits, what a later run is given,
/// and that only the one path the bot named is read.
/// </summary>
public sealed class SharedFileTests(PostgresServer postgres)
{
    #region Sharing

    [Fact]
    public async Task A_shared_file_joins_the_reply_that_asked_with_its_caption_and_belongs_to_the_conversation()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider, WriteAndShare("out/report.csv", "a,b\n1,2\n", "The numbers"));
        var threadId = await world.NewThread();
        world.StartHost();

        var runId = await world.Post(threadId, "Make me a table");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var thread = await world.ReadThread(threadId);
        var file = await world.Read(db => db.Files.AsNoTracking().SingleAsync(RunWorld.Token));
        var reply = (await world.ReadMessages(threadId)).Single(message => message.Parts.OfType<FilePart>().Any());
        var share = (await world.ReadToolCalls(runId)).Single(call => call.ToolName == "sandbox_share_file");

        share.IsError.Should().BeFalse(share.Result?.GetRawText());
        file.Should().BeEquivalentTo(
            new { Name = "report.csv", ContentType = "text/csv", Size = 8L, CreatedByBotId = (Guid?)world.BotId, CreatedByUserId = (Guid?)null, ThreadId = (Guid?)threadId, MessageId = (Guid?)reply.Id, thread.AudienceUserId },
            options => options.ExcludingMissingMembers());
        reply.Role.Should().Be(MessageRole.Assistant);
        reply.Parts.OfType<ToolCallPart>().Should().Contain(call => call.ToolCallId == share.Id, "the file sits in the reply whose call shared it");
        reply.Parts.OfType<FilePart>().Single().Should().BeEquivalentTo(new FilePart(file.Id, "report.csv", "text/csv", 8, "a,b\n1,2", "The numbers"));
        (await ReadStored(world, file)).Should().Equal(Encoding.UTF8.GetBytes("a,b\n1,2\n"));

        var announced = (await world.ReadEvents(runId))
            .Where(stored => stored.Type == RunEventTypes.MessageCompleted)
            .Select(stored => RunWorld.Data<MessageCompleted>(stored).Message)
            .Last(message => message.Id == reply.Id);
        announced.Parts.Should().Contain(part => part.Kind == "file" && part.File!.Caption == "The numbers", "the reply is announced again with the file, so it shows live");
    }

    [Fact]
    public async Task A_picture_is_shared_as_a_picture_with_its_size()
    {
        var provider = new FakeSandboxProvider();
        provider.AfterWrite = path => provider.Only().Files[path] = TestPng.Make(64, 32);
        await using var world = await SandboxWorld(provider, WriteAndShare("out/chart.png", "soon a picture", null));
        var threadId = await world.NewThread();
        world.StartHost();

        (await world.WaitForStatus(await world.Post(threadId, "Draw it"), RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var image = (await world.ReadMessages(threadId)).SelectMany(message => message.Parts).OfType<ImagePart>().Single();
        image.Should().BeEquivalentTo(new { Name = "chart.png", MediaType = "image/png", Width = (int?)64, Height = (int?)32, Caption = (string?)null }, options => options.ExcludingMissingMembers());
    }

    [Fact]
    public async Task Only_the_path_the_bot_named_is_read_and_nothing_in_the_sandbox_is_listed_or_run()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider, WriteAndShare("out/mine.txt", "mine", null));
        world.StartHost();

        (await world.WaitForStatus(await world.Post(await world.NewThread(), "Share it"), RunStatus.Completed, RunStatus.Failed))
            .Status.Should().Be(RunStatus.Completed);

        provider.Reads.Should().Equal("out/mine.txt");
        provider.Listings.Should().BeEmpty();
        provider.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task A_call_run_again_after_a_restart_finds_its_file_and_shares_no_second()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider, WriteAndShare("out/report.txt", "once", null));
        var threadId = await world.NewThread();
        var host = world.StartHost();
        var runId = await world.Post(threadId, "Share it");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var share = (await world.ReadToolCalls(runId)).Single(call => call.ToolName == "sandbox_share_file");
        var call = new BuiltinToolCall(
            JsonElement.Parse("{}"),
            world.BotId,
            runId,
            share.Id,
            host.Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { })
        {
            ThreadId = threadId,
        };

        var again = await host.Services.GetRequiredService<FileSharing>().Share(call, null, "out/report.txt", null, RunWorld.Token);

        var first = await world.Read(db => db.Files.AsNoTracking().SingleAsync(RunWorld.Token));
        again.Id.Should().Be(first.Id);
        (await world.ReadMessages(threadId)).SelectMany(message => message.Parts).OfType<FilePart>().Should().ContainSingle();
        provider.Reads.Should().ContainSingle("the file was not read a second time");
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token)))
            .Should().Be(4, "its bytes are counted once");
    }

    #endregion

    #region Audience And Taint

    [Fact]
    public async Task A_file_shared_in_a_private_conversation_reaches_no_other_member_and_one_in_a_shared_conversation_does()
    {
        var provider = new FakeSandboxProvider();
        var script = new ScriptedModel([.. WriteAndShareSteps("out/private.txt", "private"), .. WriteAndShareSteps("out/shared.txt", "shared")]);
        await using var world = await SandboxWorld(provider, script);
        var adminId = await MakeShared(world);
        var privateThread = await world.NewThread();
        var sharedThread = await world.NewSharedThread();
        world.StartHost();

        (await world.WaitForStatus(await world.Post(privateThread, "Make it"), RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        (await world.WaitForStatus(await world.Post(sharedThread, "Make it"), RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var files = await world.Read(db => db.Files.AsNoTracking().ToListAsync(RunWorld.Token));
        var privateFile = files.Single(file => file.ThreadId == privateThread);
        var sharedFile = files.Single(file => file.ThreadId == sharedThread);

        (await Open(world, world.UserId, privateFile.Id)).Should().Be("private");
        (await Open(world, adminId, privateFile.Id)).Should().BeNull("an admin who cannot see the conversation cannot fetch its file");
        (await Open(world, adminId, sharedFile.Id)).Should().Be("shared", "a member who can see the conversation can");
    }

    [Fact]
    public async Task Every_later_run_of_the_conversation_is_tainted_by_a_shared_file_and_another_conversation_is_not()
    {
        var provider = new FakeSandboxProvider();
        var script = new ScriptedModel([.. WriteAndShareSteps("out/report.txt", "Q3 totals 42"), [new TextOutput("Glad to.")], [new TextOutput("Hello.")]]);
        await using var world = await SandboxWorld(provider, script);
        var threadId = await world.NewThread();
        var otherThread = await world.NewThread();
        world.StartHost();

        var sharing = await world.WaitForStatus(await world.Post(threadId, "Make it"), RunStatus.Completed, RunStatus.Failed);
        var later = await world.WaitForStatus(await world.Post(threadId, "Thanks"), RunStatus.Completed, RunStatus.Failed);
        var elsewhere = await world.WaitForStatus(await world.Post(otherThread, "Hi"), RunStatus.Completed, RunStatus.Failed);

        sharing.Tainted.Should().BeFalse("its sandbox has no network and holds nothing a person attached");
        later.Tainted.Should().BeTrue("the conversation carries a file read out of the sandbox");
        elsewhere.Tainted.Should().BeFalse();
        world.Model.Requests[^2].Messages.SelectMany(message => message.Contents).OfType<ModelText>()
            .Should().NotContain(text => text.Text.Contains("Q3 totals 42", StringComparison.Ordinal) || text.Text.Contains("attached", StringComparison.Ordinal),
                "a later run is not handed the bot's own file as if a person had attached it; the call and its result say it was shared");
    }

    #endregion

    #region Refusals

    [Theory]
    [InlineData(false, "This file is larger than 10 bytes, the most this server takes.")]
    [InlineData(true, "This space keeps at most 1 MB of files, and this one would take it past that.")]
    public async Task A_file_past_a_limit_is_refused_with_a_sentence_the_bot_can_pass_on_and_nothing_is_kept(bool spaceAlmostFull, string sentence)
    {
        var provider = new FakeSandboxProvider();
        var policy = DeploymentPolicy.SelfHosted with { MaxFileBytes = spaceAlmostFull ? 1024 : 10, MaxSpaceFileMegabytes = 1 };
        await using var world = await SandboxWorld(provider, WriteAndShare("out/big.txt", "01234567890", null), policy);
        var held = spaceAlmostFull ? 1024L * 1024 - 5 : 0;
        await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId)
            .ExecuteUpdateAsync(set => set.SetProperty(space => space.StoredFileBytes, held), RunWorld.Token));
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Share it");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var share = (await world.ReadToolCalls(runId)).Single(call => call.ToolName == "sandbox_share_file");
        share.IsError.Should().BeTrue();
        share.Result!.Value.GetRawText().Should().Contain(sentence);
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
        world.Files.Count.Should().Be(0);
        (await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId).Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token))).Should().Be(held);
    }

    [Fact]
    public async Task A_file_that_holds_the_sandboxs_access_token_is_not_shared_and_the_token_goes_nowhere()
    {
        var provider = new FakeSandboxProvider();
        provider.AfterWrite = path =>
        {
            var sandbox = provider.Only();
            sandbox.Files[path] = Encoding.UTF8.GetBytes($"{new string('x', 70_000)}KEY={sandbox.Token}\n");
        };
        await using var world = await SandboxWorld(provider, WriteAndShare("out/env.txt", "soon the key", null));
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Share it");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var share = (await world.ReadToolCalls(runId)).Single(call => call.ToolName == "sandbox_share_file");
        share.IsError.Should().BeTrue();
        share.Result!.Value.GetRawText().Should().Contain("access key").And.NotContain(provider.Only().Token);
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
        world.Files.Count.Should().Be(0);
    }

    #endregion

    #region Helpers

    private async Task<RunWorld> SandboxWorld(FakeSandboxProvider provider, ScriptedModel script, DeploymentPolicy? policy = null)
    {
        var world = await RunWorld.Create(postgres, script, everyHost: services =>
        {
            services.AddSingleton<ISandboxProvider>(provider);
            services.AddSandboxTools();

            if (policy is not null)
            {
                services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = policy }));
            }
        });
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = SandboxPolicy.Private;
            return true;
        });
        await world.Grant("sandbox.write_file", GrantEffect.Allow);
        await world.Grant("sandbox.share_file", GrantEffect.Allow);
        return world;
    }

    private static ScriptedModel WriteAndShare(string path, string content, string? caption) =>
        new([.. WriteAndShareSteps(path, content, caption)]);

    /// <summary>The model's replies for one run: write the file, share it, then say so.</summary>
    private static IEnumerable<ModelOutput[]> WriteAndShareSteps(string path, string content, string? caption = null)
    {
        var write = JsonSerializer.Serialize(new { path, content });
        var share = caption is null ? JsonSerializer.Serialize(new { path }) : JsonSerializer.Serialize(new { path, caption });
        yield return [new ToolCallOutput("c1", "sandbox_write_file", write)];
        yield return [new ToolCallOutput("c2", "sandbox_share_file", share)];
        yield return [new TextOutput("Here it is.")];
    }

    private static async Task<byte[]> ReadStored(RunWorld world, StoredFile file)
    {
        await using var content = await world.Files.Open(world.SpaceId, file.StorageKey, RunWorld.Token);
        using var copy = new MemoryStream();
        await content!.CopyToAsync(copy, RunWorld.Token);
        return copy.ToArray();
    }

    /// <summary>The file's text as <paramref name="userId"/> fetches it through the engine, or null when they cannot.</summary>
    private static Task<string?> Open(RunWorld world, Guid userId, Guid fileId) =>
        world.WithServiceAs<FileService, string?>(userId, async files =>
        {
            if (await files.Open(fileId, RunWorld.Token) is not { } download)
            {
                return null;
            }

            await using var content = download.Content;
            using var reader = new StreamReader(content);
            return await reader.ReadToEndAsync(RunWorld.Token);
        });

    /// <summary>Makes the world's space a shared one with an admin besides its owner. Returns the admin.</summary>
    private static async Task<Guid> MakeShared(RunWorld world)
    {
        await world.Read(db => db.Spaces.Where(space => space.Id == world.SpaceId)
            .ExecuteUpdateAsync(set => set.SetProperty(space => space.Kind, SpaceKind.Shared), RunWorld.Token));
        return await world.AddMember(MembershipRole.Admin);
    }

    #endregion
}
