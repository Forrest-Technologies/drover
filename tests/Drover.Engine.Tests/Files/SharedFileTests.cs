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

    [Fact]
    public async Task Several_shares_into_one_reply_at_once_each_keep_their_part_and_the_reply_holds_no_more_than_the_limit()
    {
        var provider = new FakeSandboxProvider();
        var writes = Enumerable.Range(0, 4)
            .Select(index => (ModelOutput)new ToolCallOutput($"w{index}", "sandbox_write_file", JsonSerializer.Serialize(new { path = $"out/{index}.txt", content = $"file {index}" })))
            .ToArray();
        await using var world = await SandboxWorld(provider, new ScriptedModel(writes, [new TextOutput("Written.")]), DeploymentPolicy.SelfHosted with { MaxFilesPerMessage = 3 });
        var threadId = await world.NewThread();
        var host = world.StartHost();
        var runId = await world.Post(threadId, "Write four files");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        Guid[] callIds = [.. Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7())];
        var replyId = await AddReplyWithShares(world, threadId, runId, callIds);
        var sharing = host.Services.GetRequiredService<FileSharing>();

        var attempts = await Task.WhenAll(callIds.Select((callId, index) => TryShare(sharing, ShareCall(world, host, threadId, runId, callId), $"out/{index}.txt")));

        attempts.Count(refusal => refusal is null).Should().Be(3);
        attempts.Should().ContainSingle(refusal => refusal != null).Which.Should().Contain("This reply carries that many already.");
        var reply = (await world.ReadMessages(threadId)).Single(message => message.Id == replyId);
        reply.Parts.OfType<FilePart>().Should().HaveCount(3, "each share read the reply under its lock, so none was lost");
        (await world.Read(db => db.Files.AsNoTracking().ToListAsync(RunWorld.Token)))
            .Should().HaveCount(3).And.OnlyContain(file => file.MessageId == replyId);
        world.Files.Count.Should().Be(3, "the refused file's bytes are not kept");
    }

    [Fact]
    public async Task A_share_in_the_same_batch_as_the_write_that_makes_its_file_waits_for_the_write()
    {
        var provider = new FakeSandboxProvider { BeforeWrite = (_, cancellationToken) => Task.Delay(300, cancellationToken) };
        var script = new ScriptedModel(
            [
                new ToolCallOutput("c1", "sandbox_write_file", """{"path":"out/slow.txt","content":"made first"}"""),
                new ToolCallOutput("c2", "sandbox_share_file", """{"path":"out/slow.txt"}"""),
            ],
            [new TextOutput("Here it is.")]);
        await using var world = await SandboxWorld(provider, script);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Make it and share it");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        (await world.ReadToolCalls(runId)).Single(call => call.ToolName == "sandbox_share_file").IsError.Should().BeFalse("sharing changes the conversation, so it waits for the earlier write");
        (await ReadStored(world, await world.Read(db => db.Files.AsNoTracking().SingleAsync(RunWorld.Token)))).Should().Equal(Encoding.UTF8.GetBytes("made first"));
    }

    [Fact]
    public async Task An_approved_share_cut_off_by_a_kill_runs_again_once_without_asking_again_and_keeps_one_file()
    {
        var provider = new FakeSandboxProvider();
        var paused = new TaskCompletionSource();
        var reads = 0;
        provider.BeforeRead = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref reads) == 1)
            {
                paused.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        };
        await using var world = await SandboxWorld(provider, WriteAndShare("out/report.txt", "the report", null), share: GrantEffect.Ask);
        var threadId = await world.NewThread();
        var first = world.StartHost();
        var runId = await world.Post(threadId, "Share it");
        (await world.WaitForStatus(runId, RunStatus.Waiting, RunStatus.Failed, RunStatus.Completed)).Status.Should().Be(RunStatus.Waiting);
        var asked = (await world.ReadToolCalls(runId)).Single(found => found.Status == ToolCallStatus.PendingApproval);

        await world.Decide(asked.Id, approved: true);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(30), RunWorld.Token);
        var deadWorker = first.WorkerId;
        await first.Kill();
        world.StartHost();
        await world.ExpireLeaseOf(deadWorker, runId);
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed, RunStatus.Waiting);

        run.Status.Should().Be(RunStatus.Completed, "the person's yes still holds for the call it was given to");
        provider.Reads.Should().Equal("out/report.txt", "out/report.txt");
        (await world.ReadToolCalls(runId)).Single(call => call.Id == asked.Id).Status.Should().Be(ToolCallStatus.Completed);
        (await world.ReadEvents(runId)).Count(stored => stored.Type == RunEventTypes.ToolApprovalRequested).Should().Be(1, "nobody is asked a second time");
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(1);
        (await world.ReadMessages(threadId)).SelectMany(message => message.Parts).OfType<FilePart>().Should().ContainSingle();
    }

    #endregion

    #region Audience And Taint

    [Fact]
    public async Task A_file_shared_in_a_private_conversation_reaches_no_other_member_and_one_in_a_shared_conversation_does()
    {
        var provider = new FakeSandboxProvider();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "sandbox_share_file", """{"path":"out/private.txt"}""")],
            [new TextOutput("Here it is.")],
            [new ToolCallOutput("c1", "sandbox_write_file", """{"path":"out/shared.txt","content":"shared"}""")],
            [new ToolCallOutput("c2", "sandbox_share_file", """{"path":"out/shared.txt"}""")],
            [new TextOutput("Here it is.")]);
        await using var world = await SandboxWorld(provider, script);
        var adminId = await MakeShared(world);
        var privateThread = await world.NewThread();
        var sharedThread = await world.NewSharedThread();
        world.StartHost();

        // A person puts the private file there. A bot's own write from a private conversation is refused (#185), and the
        // stand-in provider does not run a shell, so the file is placed on the sandbox the person's command made.
        var control = world.Hosts[0].Services.GetRequiredService<SandboxControlService>();
        var put = await control.Run(world.SpaceId, world.UserId, world.BotId, "printf private > out/private.txt", RunWorld.Token);
        put.Refusal.Should().Be(SandboxControlRefusal.None, put.Problem);
        provider.Only().Files["out/private.txt"] = "private"u8.ToArray();

        (await world.WaitForStatus(await world.Post(privateThread, "Share it"), RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
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

    [Fact]
    public async Task A_tainted_run_asks_before_it_shares_a_file()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider, new ScriptedModel([new ToolCallOutput("c1", "sandbox_share_file", """{"path":"out/other.txt"}""")]));
        var threadId = await world.NewThread();
        var notes = await world.Upload(threadId, "notes.txt", "a person's notes"u8.ToArray());
        var host = world.StartHost();
        await host.Services.GetRequiredService<SandboxBroker>().WriteFile(ShareCall(world, host, threadId, Guid.CreateVersion7(), Guid.CreateVersion7()), null, "out/other.txt", "made elsewhere", RunWorld.Token);

        var post = await world.PostRaw(threadId, "Share what you have", fileIds: [notes.File!.Id]);
        var run = await world.WaitForStatus(post.Run!.Id, RunStatus.Waiting, RunStatus.Completed, RunStatus.Failed);

        run.Tainted.Should().BeTrue();
        run.Status.Should().Be(RunStatus.Waiting, "sharing is a write, and a tainted run asks before it writes");
        (await world.ReadToolCalls(run.Id)).Single(call => call.ToolName == "sandbox_share_file").Status.Should().Be(ToolCallStatus.PendingApproval);
        (await world.Read(db => db.Files.CountAsync(file => file.CreatedByBotId != null, RunWorld.Token))).Should().Be(0);
    }

    [Fact]
    public async Task A_bot_working_for_another_bot_may_not_share_and_is_told_to_hand_the_file_back()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider, new ScriptedModel());
        var childId = await world.NewBotBeneath(world.BotId, "Finder", "Makes reports.");
        await world.Read(async db =>
        {
            var child = await db.Bots.SingleAsync(found => found.Id == childId, RunWorld.Token);
            child.SandboxPolicy = SandboxPolicy.Private;
            return true;
        });
        await world.Grant(DelegateTool.Name, GrantEffect.Allow);
        await world.GrantTo(childId, "sandbox.share_file", GrantEffect.Allow);
        world.Script.Then(
            [new ToolCallOutput("d1", DelegateTool.Name, JsonSerializer.Serialize(new { bot_id = childId, task = "Make the report." }))],
            [new ToolCallOutput("c1", "sandbox_share_file", """{"path":"out/report.txt"}""")],
            [new TextOutput("The report is at out/report.txt.")],
            [new TextOutput("Finder made it.")]);
        world.StartHost();

        var rootId = await world.Post(await world.NewThread(), "Get Finder to make the report");
        (await world.WaitForStatus(rootId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var child = (await world.ReadTree(rootId)).Single(run => run.BotId == childId);
        var share = (await world.ReadToolCalls(child.Id)).Single(call => call.ToolName == "sandbox_share_file");
        share.IsError.Should().BeTrue();
        share.Result!.Value.GetRawText().Should().Contain("Work handed to you by another bot cannot share files with the person.");
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
        provider.Reads.Should().BeEmpty("nothing is read for a share that is refused");
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

    private async Task<RunWorld> SandboxWorld(
        FakeSandboxProvider provider,
        ScriptedModel script,
        DeploymentPolicy? policy = null,
        GrantEffect share = GrantEffect.Allow)
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
        await world.Grant("sandbox.share_file", share);
        return world;
    }

    /// <summary>A share call of the world's person, made directly, in a run and thread the test names.</summary>
    private static BuiltinToolCall ShareCall(RunWorld world, EngineHost host, Guid threadId, Guid runId, Guid toolCallId) =>
        new(
            JsonElement.Parse("{}"),
            world.BotId,
            runId,
            toolCallId,
            host.Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { })
        {
            ThreadId = threadId,
        };

    /// <summary>The refusal a share answered with, or null when it shared.</summary>
    private static async Task<string?> TryShare(FileSharing sharing, BuiltinToolCall call, string path)
    {
        try
        {
            await sharing.Share(call, null, path, null, RunWorld.Token);
            return null;
        }
        catch (BuiltinToolException refused)
        {
            return refused.Message;
        }
    }

    /// <summary>A reply of the run, after its others, whose calls are shares with these ids, as one model step would make it.</summary>
    private static Task<Guid> AddReplyWithShares(RunWorld world, Guid threadId, Guid runId, IReadOnlyList<Guid> callIds) =>
        world.Transact(async services =>
        {
            var thread = (await services.GetRequiredService<ThreadRepository>().FindThread(threadId, RunWorld.Token))!;
            var reply = new Message
            {
                Role = MessageRole.Assistant,
                RunId = runId,
                AuthorBotId = world.BotId,
                Parts = [.. callIds.Select((callId, index) => (MessagePart)new ToolCallPart(callId, $"s{index}", "sandbox_share_file", JsonElement.Parse("{}")))],
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await services.GetRequiredService<MessageRepository>().AppendMessages(thread, [reply], RunWorld.Token);
            return reply.Id;
        });

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
