namespace Drover.Engine.Tests.Files;

/// <summary>
/// Files a person attaches: what the model is given for each kind, what is refused before sending and why, and the
/// limits of the deployment policy.
/// </summary>
public sealed class AttachedFileTests(PostgresServer postgres)
{
    #region What The Model Gets

    [Fact]
    public async Task A_picture_reaches_a_model_that_reads_pictures_as_the_picture_itself()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("A cat.")]));
        await SetVision(world, true);
        var threadId = await world.NewThread();
        var png = TestPng.Make(64, 48);

        var upload = await world.Upload(threadId, "cat.png", png);
        upload.File.Should().NotBeNull(upload.Detail);
        upload.File!.ContentType.Should().Be("image/png");
        (upload.File.Width, upload.File.Height).Should().Be((64, 48));
        world.StartHost();
        var runId = await PostWith(world, threadId, "What is this?", upload.File.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var seen = world.Model.ImagesSeen[0].Should().ContainSingle().Which;
        seen.MediaType.Should().Be("image/png");
        seen.Data.ToArray().Should().Equal(png);
        TextsOf(world.Model.Requests[0]).Should().Contain(text => text.Contains("cat.png", StringComparison.Ordinal) && text.Contains("64 by 48", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_text_file_reaches_the_model_as_its_text()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Read it.")]));
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "notes.md", Encoding.UTF8.GetBytes("# Plan\nShip on Friday."));
        world.StartHost();

        var runId = await PostWith(world, threadId, string.Empty, upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var text = TextsOf(world.Model.Requests[0]).Should().ContainSingle(found => found.Contains("notes.md", StringComparison.Ordinal)).Which;
        text.Should().Contain("# Plan\nShip on Friday.").And.Contain("[End of notes.md.]");
        world.Model.ImagesSeen[0].Should().BeEmpty();
    }

    [Fact]
    public async Task A_long_text_file_is_cut_at_the_bound_and_the_model_is_told_so()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("Read it.")]));
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "log.txt", Encoding.UTF8.GetBytes(new string('a', RunFiles.MaxTextCharacters + 5000)));
        world.StartHost();

        var runId = await PostWith(world, threadId, "Summarize", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var text = TextsOf(world.Model.Requests[0]).Single(found => found.Contains("log.txt", StringComparison.Ordinal));
        text.Should().Contain(new string('a', RunFiles.MaxTextCharacters)).And.NotContain(new string('a', RunFiles.MaxTextCharacters + 1));
        text.Should().Contain($"Only its first {RunFiles.MaxTextCharacters} characters are shown.");
    }

    [Fact]
    public async Task Another_kind_lands_in_the_bots_sandbox_and_the_model_is_given_its_path()
    {
        var sandbox = new FakeSandboxProvider();
        await using var world = await SandboxWorld(sandbox, new ScriptedModel([new TextOutput("Got it.")]));
        var threadId = await world.NewThread();
        byte[] pdf = [.. "%PDF-1.7\n"u8, 0, 1, 2, 3];
        var upload = await world.Upload(threadId, "report.pdf", pdf);
        upload.File.Should().NotBeNull(upload.Detail);
        world.StartHost();

        var runId = await PostWith(world, threadId, "Read the report", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        var path = $"attachments/{upload.File.Id:N}/report.pdf";
        sandbox.Only().Files[path].Should().Equal(pdf);
        TextsOf(world.Model.Requests[0]).Should().Contain(text => text.Contains($"in your sandbox at {path}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_picture_for_a_model_that_cannot_read_pictures_goes_to_the_sandbox_when_there_is_one()
    {
        var sandbox = new FakeSandboxProvider();
        await using var world = await SandboxWorld(sandbox, new ScriptedModel([new TextOutput("Got it.")]));
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "chart.png", TestPng.Make(10, 10));
        world.StartHost();

        var runId = await PostWith(world, threadId, "Look", upload.File!.Id);

        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);
        world.Model.ImagesSeen[0].Should().BeEmpty();
        sandbox.Only().Files.Should().ContainKey($"attachments/{upload.File.Id:N}/chart.png");
    }

    #endregion

    #region Refused Before Sending

    [Fact]
    public async Task A_picture_is_refused_on_upload_when_the_model_cannot_read_pictures_and_there_is_no_sandbox()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = await world.NewThread();

        var upload = await world.Upload(threadId, "cat.png", TestPng.Make(4, 4));

        upload.Problem.Should().Be(FileProblem.Refused);
        upload.Refusal.Should().Be(FileRefusals.ModelCannotReadImages);
        upload.Detail.Should().Contain("cannot read pictures");
        world.Files.Count.Should().Be(0, "a refused file is not kept");
    }

    [Fact]
    public async Task Another_kind_is_refused_on_upload_when_the_bot_has_no_sandbox()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = await world.NewThread();

        var upload = await world.Upload(threadId, "archive.zip", [0x50, 0x4B, 0x03, 0x04, 0, 0, 0xFF]);

        upload.Refusal.Should().Be(FileRefusals.NothingCanOpenIt);
        upload.Detail.Should().Contain("needs a sandbox");
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
    }

    [Fact]
    public async Task A_file_cannot_be_sent_by_someone_who_did_not_upload_it_or_in_another_conversation_or_twice()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel([new TextOutput("One.")]));
        var threadId = await world.NewThread();
        var otherThread = await world.NewThread();
        var upload = await world.Upload(threadId, "a.txt", "hello"u8.ToArray());

        var elsewhere = await world.PostRaw(otherThread, "here", fileIds: [upload.File!.Id]);
        var unknown = await world.PostRaw(threadId, "here", fileIds: [Guid.CreateVersion7()]);
        var sent = await world.PostRaw(threadId, "first", fileIds: [upload.File.Id]);
        var again = await world.PostRaw(threadId, "again", fileIds: [upload.File.Id]);

        elsewhere.Problem.Should().Be(PostProblem.Invalid);
        unknown.Problem.Should().Be(PostProblem.Invalid);
        sent.Problem.Should().Be(PostProblem.None);
        again.Problem.Should().Be(PostProblem.Invalid);
        again.Detail.Should().Contain("sent already");
        (await world.Read(db => db.Files.SingleAsync(RunWorld.Token))).MessageId.Should().Be(sent.Message!.Id);
    }

    [Fact]
    public async Task A_file_already_uploaded_is_refused_when_sent_if_the_bot_can_no_longer_open_it()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        await SetVision(world, true);
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "cat.png", TestPng.Make(4, 4));
        await SetVision(world, false);

        var post = await world.PostRaw(threadId, "look", fileIds: [upload.File!.Id]);

        post.Problem.Should().Be(PostProblem.Invalid);
        post.Detail.Should().Be("cat.png: Helper's model cannot read pictures, and Helper has no sandbox to put this one in.");
    }

    [Fact]
    public async Task Removing_a_file_before_sending_deletes_it_and_gives_its_bytes_back()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel());
        var threadId = await world.NewThread();
        var upload = await world.Upload(threadId, "a.txt", new byte[1000]);
        upload.Problem.Should().Be(FileProblem.Refused, "NUL bytes are not text, so a bot with no sandbox cannot open it");
        var text = await world.Upload(threadId, "b.txt", Encoding.UTF8.GetBytes(new string('b', 1000)));

        var removed = await RemovePending(world, text.File!.Id, world.UserId);

        removed.Should().BeTrue();
        world.Files.Count.Should().Be(0);
        (await world.Read(db => db.Spaces.Select(space => space.StoredFileBytes).SingleAsync(RunWorld.Token))).Should().Be(0);
    }

    #endregion

    #region Limits

    [Fact]
    public async Task A_file_past_MaxFileBytes_is_refused_with_a_sentence_naming_the_limit_whether_or_not_its_length_was_declared()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = new DeploymentPolicy { MaxFileBytes = 2048 } })));
        var threadId = await world.NewThread();
        var content = Encoding.UTF8.GetBytes(new string('x', 3000));

        var declared = await world.Upload(threadId, "big.txt", content);
        var streamed = await world.Upload(threadId, "big.txt", content, declareLength: false);

        declared.Refusal.Should().Be(FileRefusals.TooLarge);
        declared.Detail.Should().Be("This file is 2.9 KB. This server takes files up to 2 KB.");
        streamed.Refusal.Should().Be(FileRefusals.TooLarge);
        streamed.Detail.Should().Be("This file is larger than 2 KB, the most this server takes.");
        world.Files.Count.Should().Be(0);
    }

    [Fact]
    public async Task A_message_with_more_files_than_MaxFilesPerMessage_is_refused_with_a_sentence_naming_the_limit()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = new DeploymentPolicy { MaxFilesPerMessage = 2 } })));
        var threadId = await world.NewThread();
        List<Guid> ids = [];

        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
        {
            ids.Add((await world.Upload(threadId, name, "x"u8.ToArray())).File!.Id);
        }

        var post = await world.PostRaw(threadId, "three", fileIds: ids);

        post.Problem.Should().Be(PostProblem.Invalid);
        post.Detail.Should().Be("A message carries at most 2 files on this server.");
    }

    [Fact]
    public async Task A_file_that_would_take_the_space_past_MaxSpaceFileMegabytes_is_refused_with_a_sentence_naming_the_limit()
    {
        await using var world = await RunWorld.Create(postgres, new ScriptedModel(), everyHost: services =>
            services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = new DeploymentPolicy { MaxSpaceFileMegabytes = 1 } })));
        var threadId = await world.NewThread();
        var half = Encoding.UTF8.GetBytes(new string('h', 600 * 1024));

        var first = await world.Upload(threadId, "one.txt", half);
        var declared = await world.Upload(threadId, "two.txt", half);
        var streamed = await world.Upload(threadId, "two.txt", half, declareLength: false);

        first.File.Should().NotBeNull();
        declared.Refusal.Should().Be(FileRefusals.SpaceFull);
        declared.Detail.Should().Be("This space keeps at most 1 MB of files, and this one would take it past that.");
        streamed.Refusal.Should().Be(FileRefusals.SpaceFull, "the total is checked again when the row is stored");
        world.Files.Count.Should().Be(1);
    }

    #endregion

    #region Private Methods

    private static IEnumerable<string> TextsOf(ModelChatRequest request) =>
        request.Messages.SelectMany(message => message.Contents).OfType<ModelText>().Select(text => text.Text);

    private static async Task<Guid> PostWith(RunWorld world, Guid threadId, string text, Guid fileId)
    {
        var post = await world.PostRaw(threadId, text, fileIds: [fileId]);
        post.Problem.Should().Be(PostProblem.None, post.Detail);
        return post.Run!.Id;
    }

    private static Task<bool> SetVision(RunWorld world, bool vision) =>
        world.Read(async db =>
        {
            var model = await db.ModelCatalog.SingleAsync(entry => entry.Model == "scripted", RunWorld.Token);
            model.Capabilities = new ModelCapabilities { Vision = vision };
            return true;
        });

    private static async Task<bool> RemovePending(RunWorld world, Guid fileId, Guid userId)
    {
        await using var scope = world.Hosts[0].Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, userId);
        return await scope.ServiceProvider.GetRequiredService<FileService>().RemovePending(fileId, userId, RunWorld.Token);
    }

    private async Task<RunWorld> SandboxWorld(FakeSandboxProvider provider, ScriptedModel script)
    {
        var world = await RunWorld.Create(postgres, script, everyHost: services =>
        {
            services.AddSingleton<ISandboxProvider>(provider);
            services.AddSandboxTools();
        });
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = SandboxPolicy.Private;
            return true;
        });
        await world.Grant("sandbox.read_file", GrantEffect.Allow);
        return world;
    }

    #endregion
}
