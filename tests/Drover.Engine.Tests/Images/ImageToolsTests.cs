namespace Drover.Engine.Tests.Images;

/// <summary>
/// Image creation in a real run over PostgreSQL: its grant and deployment gate, stored conversation file, provider
/// request, and billed usage.
/// </summary>
public sealed class ImageToolsTests(PostgresServer postgres)
{
    #region Granted

    [Theory]
    [InlineData(true, "USD")]
    [InlineData(false, "EUR")]
    public async Task A_granted_image_tool_creates_a_picture_and_uses_the_billing_source_currency(
        bool hasDirectCost,
        string expectedCurrency)
    {
        var generator = new FixedImageGenerator
        {
            Usage = hasDirectCost
                ? new ModelImageUsage { Cost = 0.07m }
                : new ModelImageUsage { InputTokens = 1, OutputTokens = 2 },
        };
        var script = new ScriptedModel(
            [new ToolCallOutput(
                "c1",
                "image_create",
                """
                {
                  "prompt": "A green barn at sunrise",
                  "model": "picture-one",
                  "size": "1536x1024",
                  "caption": "Morning barn"
                }
                """)],
            [new TextOutput("Here it is.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services => services.AddSingleton<IModelImageGenerator>(generator));
        await world.Read(db =>
        {
            db.ModelCatalog.Add(new ModelCatalogEntry
            {
                ModelConnectionId = world.ConnectionId,
                Model = "picture-one",
                DisplayName = "Picture One",
                Capabilities = new ModelCapabilities { ImageGeneration = true },
                Pricing = new ModelPricing
                {
                    Currency = "EUR",
                    InputPerMillion = 10_000,
                    OutputPerMillion = 30_000,
                },
            });
            return Task.FromResult(true);
        });
        await world.Grant(ImageTools.CreateName, GrantEffect.Allow);
        var threadId = await world.NewThread();
        var host = world.StartHost();

        var runId = await world.Post(threadId, "Create the barn picture.");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        world.Model.ToolsOffered[0].Should().Contain("image_create");
        var toolCall = (await world.ReadToolCalls(runId)).Should().ContainSingle().Which;
        toolCall.IsError.Should().BeFalse(toolCall.Result?.GetRawText());
        generator.Requests.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new ModelImageRequest("A green barn at sunrise", "picture-one", "1536x1024", 25 * 1024 * 1024));
        generator.Keys.Should().ContainSingle().Which.Should().Be(world.ModelKey);
        generator.Policies.Should().ContainSingle().Which.Should().NotBeNull();
        generator.Endpoints.Should().ContainSingle().Which.Should().Match<ModelEndpoint>(endpoint =>
            endpoint.Kind == ScriptedModelProvider.ConnectionKind
            && endpoint.BaseAddress == world.Model.FixedAddress);

        var file = await world.Read(db => db.Files.AsNoTracking().SingleAsync(RunWorld.Token));
        var reply = (await world.ReadMessages(threadId)).Single(message => message.Parts.OfType<ImagePart>().Any());
        var image = reply.Parts.OfType<ImagePart>().Single();
        image.Should().BeEquivalentTo(
            new
            {
                FileId = file.Id,
                MediaType = "image/png",
                Name = "generated-image.png",
                Size = 33L,
                Width = (int?)64,
                Height = (int?)32,
                Caption = "Morning barn",
            });
        file.Should().BeEquivalentTo(
            new
            {
                Name = "generated-image.png",
                ContentType = "image/png",
                Size = 33L,
                CreatedByBotId = (Guid?)world.BotId,
                CreatedByUserId = (Guid?)null,
                ThreadId = (Guid?)threadId,
                MessageId = (Guid?)reply.Id,
                reply.AudienceUserId,
            },
            options => options.ExcludingMissingMembers());
        world.Files.Count.Should().Be(1);

        var usage = (await world.ReadUsage(runId)).Single(record => record.Model == "picture-one");
        usage.Should().BeEquivalentTo(
            new
            {
                ModelConnectionId = world.ConnectionId,
                InputTokens = hasDirectCost ? 0L : 1L,
                OutputTokens = hasDirectCost ? 0L : 2L,
                Cost = (decimal?)0.07m,
                Currency = expectedCurrency,
            },
            options => options.ExcludingMissingMembers());

        var sharing = host.Services.GetRequiredService<GeneratedImageSharing>();
        var failedAnnouncement = RunCall(world, host, threadId, runId, toolCall.Id) with
        {
            AnnounceMessage = _ => throw new InvalidOperationException("Watcher unavailable."),
        };
        var announce = async () => await sharing.Share(
            failedAnnouncement,
            _ => throw new InvalidOperationException("A retry must not generate another image."),
            null,
            RunWorld.Token);

        await announce.Should().ThrowAsync<InvalidOperationException>();
        world.Files.Count.Should().Be(1, "announcement failure must not delete committed bytes");
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(1);

        var announced = new ConcurrentQueue<Guid>();
        var retry = RunCall(world, host, threadId, runId, toolCall.Id) with
        {
            AnnounceMessage = message =>
            {
                announced.Enqueue(message.Id);
                return Task.CompletedTask;
            },
        };
        var retryFile = await sharing.Share(
            retry,
            _ => throw new InvalidOperationException("A retry must not generate another image."),
            null,
            RunWorld.Token);

        retryFile.Id.Should().Be(file.Id);
        announced.Should().Equal(reply.Id);
        world.Files.Saved.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_replays_keep_the_winners_blob_and_remove_only_the_losing_copy()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new FixedImageGenerator
        {
            BeforeReturn = async cancellationToken =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            },
        };
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "image_create", """{"prompt":"A green barn","model":"picture-one"}""")],
            [new TextOutput("Here it is.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services => services.AddSingleton<IModelImageGenerator>(generator));
        await SeedImageModel(world, "picture-one");
        await world.Grant(ImageTools.CreateName, GrantEffect.Allow);
        var threadId = await world.NewThread();
        var host = world.StartHost();
        var runId = await world.Post(threadId, "Create the barn picture.");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), RunWorld.Token);
        var toolCall = (await world.ReadToolCalls(runId)).Should().ContainSingle().Which;

        try
        {
            var winner = await host.Services.GetRequiredService<GeneratedImageSharing>().Share(
                RunCall(world, host, threadId, runId, toolCall.Id),
                _ => Task.FromResult(new ModelImageResult(TestPng.Make(64, 32), "image/png", "picture-one")),
                null,
                RunWorld.Token);
            winner.Id.Should().Be(toolCall.Id);
        }
        finally
        {
            release.TrySetResult();
        }

        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        (await world.Read(db => db.Files.AsNoTracking().ToListAsync(RunWorld.Token)))
            .Should().ContainSingle().Which.Id.Should().Be(toolCall.Id);
        world.Files.Count.Should().Be(1, "the losing copy has its own storage key and is removed");
        world.Files.Saved.Should().Be(2, "both attempts may copy bytes before the reply lock chooses one row");
    }

    #endregion

    #region Not Offered

    [Fact]
    public async Task Without_a_grant_the_image_tool_is_not_sent_to_the_model()
    {
        var generator = new FixedImageGenerator();
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("I cannot create that.")]),
            everyHost: services => services.AddSingleton<IModelImageGenerator>(generator));
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Create a picture.");
        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        (await world.Read(db => db.Tools.CountAsync(tool => tool.Name == ImageTools.CreateName, RunWorld.Token)))
            .Should().Be(1, "the tool remains in the catalog so a person can grant it");
        world.Model.ToolsOffered[0].Should().NotContain("image_create");
        generator.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_server_without_image_generation_does_not_offer_the_granted_tool_and_refuses_a_call()
    {
        var policy = DeploymentPolicy.SelfHosted with { AllowImageGeneration = false };
        var generator = new FixedImageGenerator();
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("I cannot create that.")]),
            everyHost: services =>
            {
                services.AddSingleton<IOptions<DeploymentPolicyOptions>>(
                    Options.Create(new DeploymentPolicyOptions { Rules = policy }));
                services.AddSingleton<IModelImageGenerator>(generator);
            });
        await world.Grant(ImageTools.CreateName, GrantEffect.Allow);
        var host = world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Create a picture.");
        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
        var call = async () => await Tool(host).Run(Call(world, host), RunWorld.Token);

        world.Model.ToolsOffered[0].Should().NotContain("image_create");
        (await call.Should().ThrowAsync<BuiltinToolException>())
            .Which.Message.Should().Contain(nameof(DeploymentPolicy.AllowImageGeneration));
        generator.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_call_on_a_connection_of_a_kind_that_was_switched_off_is_refused_in_the_operators_words_and_never_reaches_the_provider()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new FixedImageGenerator
        {
            BeforeReturn = async cancellationToken =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            },
        };
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "image_create", """{"prompt":"A green barn","model":"picture-one"}""")],
            [new TextOutput("Here it is.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services => services.AddSingleton<IModelImageGenerator>(generator));
        await SeedImageModel(world, "picture-one");
        await world.Grant(ImageTools.CreateName, GrantEffect.Allow);
        var threadId = await world.NewThread();
        world.StartHost();
        var runId = await world.Post(threadId, "Create the barn picture.");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), RunWorld.Token);
        var toolCall = (await world.ReadToolCalls(runId)).Should().ContainSingle().Which;
        var switchedOff = world.IdleHost(services => services.AddSingleton<IOptions<DeploymentPolicyOptions>>(
            Options.Create(new DeploymentPolicyOptions
            {
                Rules = DeploymentPolicy.SelfHosted with { ModelProviders = ModelProviderSet.OperatorList },
            })));

        try
        {
            var call = async () => await Tool(switchedOff).Run(
                RunCall(world, switchedOff, threadId, runId, toolCall.Id) with
                {
                    Arguments = JsonElement.Parse("""{"prompt":"A barn","model":"picture-one"}"""),
                },
                RunWorld.Token);

            (await call.Should().ThrowAsync<BuiltinToolException>())
                .Which.Message.Should().Be($"The operator of this server switched {ScriptedModelProvider.ConnectionKind} off.");
            generator.Requests.Should().ContainSingle("only the run that was already going reached the provider");
        }
        finally
        {
            release.TrySetResult();
        }

        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);
    }

    #endregion

    #region Catalog And Limits

    [Theory]
    [InlineData("unlisted-model")]
    [InlineData("chat-only")]
    [InlineData("retired-picture")]
    public async Task Refuses_models_not_enabled_as_image_models_in_the_catalog_and_lists_allowed_ones(string requestedModel)
    {
        var generator = new FixedImageGenerator();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "image_create", $$"""{"prompt":"A green barn","model":"{{requestedModel}}"}""")],
            [new TextOutput("The model was refused.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services => services.AddSingleton<IModelImageGenerator>(generator));
        await world.Read(db =>
        {
            db.ModelCatalog.AddRange(
                new ModelCatalogEntry
                {
                    ModelConnectionId = world.ConnectionId,
                    Model = "picture-one",
                    DisplayName = "Picture One",
                    Capabilities = new ModelCapabilities { ImageGeneration = true },
                    State = AvailabilityState.Available,
                },
                new ModelCatalogEntry
                {
                    ModelConnectionId = world.ConnectionId,
                    Model = "chat-only",
                    DisplayName = "Chat Only",
                    Capabilities = new ModelCapabilities { ImageGeneration = false },
                    State = AvailabilityState.Available,
                },
                new ModelCatalogEntry
                {
                    ModelConnectionId = world.ConnectionId,
                    Model = "retired-picture",
                    DisplayName = "Retired Picture",
                    Capabilities = new ModelCapabilities { ImageGeneration = true },
                    State = AvailabilityState.Missing,
                });
            return Task.FromResult(true);
        });
        await world.Grant(ImageTools.CreateName, GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Create a barn picture.");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var toolCall = (await world.ReadToolCalls(runId)).Should().ContainSingle().Which;
        toolCall.IsError.Should().BeTrue();
        var errorText = toolCall.Result!.Value.GetProperty("error").GetString()!;
        errorText.Should().Contain("picture-one");
        errorText.Should().NotContain("chat-only");
        errorText.Should().NotContain("retired-picture");
        generator.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_image_generation_when_the_connection_catalog_has_no_image_models()
    {
        var generator = new FixedImageGenerator();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "image_create", """{"prompt":"A green barn","model":"picture-one"}""")],
            [new TextOutput("No image models.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services => services.AddSingleton<IModelImageGenerator>(generator));
        await world.Grant(ImageTools.CreateName, GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Create a barn picture.");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var toolCall = (await world.ReadToolCalls(runId)).Should().ContainSingle().Which;
        toolCall.IsError.Should().BeTrue();
        toolCall.Result!.Value.GetProperty("error").GetString().Should().Contain("no enabled image models");
        generator.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Refuses_a_delegated_run_before_generating_an_image()
    {
        var generator = new FixedImageGenerator();
        await using var world = await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("unused")]),
            everyHost: services => services.AddSingleton<IModelImageGenerator>(generator));
        await SeedImageModel(world, "picture-one");
        var threadId = await world.NewThread();
        var host = world.IdleHost();

        var call = async () => await Tool(host).Run(
            Call(world, host) with { ThreadId = threadId, IsDelegated = true },
            RunWorld.Token);

        (await call.Should().ThrowAsync<BuiltinToolException>())
            .Which.Message.Should().Contain("cannot add files to the person's conversation");
        generator.Requests.Should().BeEmpty();
        world.Files.Count.Should().Be(0);
    }

    [Fact]
    public async Task Enforces_the_per_message_file_limit_for_generated_images()
    {
        var policy = DeploymentPolicy.SelfHosted with { MaxFilesPerMessage = 1 };
        var generator = new FixedImageGenerator();
        var script = new ScriptedModel(
            [
                new ToolCallOutput("c1", "image_create", """{"prompt":"First barn","model":"picture-one"}"""),
                new ToolCallOutput("c2", "image_create", """{"prompt":"Second barn","model":"picture-one"}"""),
            ],
            [new TextOutput("Only one fit.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services =>
            {
                services.AddSingleton<IOptions<DeploymentPolicyOptions>>(
                    Options.Create(new DeploymentPolicyOptions { Rules = policy }));
                services.AddSingleton<IModelImageGenerator>(generator);
            });
        await SeedImageModel(world, "picture-one");
        await world.Grant(ImageTools.CreateName, GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Create two barn pictures.");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var toolCalls = await world.ReadToolCalls(runId);
        toolCalls.Should().HaveCount(2);
        toolCalls.Should().ContainSingle(call => !call.IsError);
        var second = toolCalls.Single(call => call.IsError);
        second.Result!.Value.GetProperty("error").GetString().Should().Contain("already");
        generator.Requests.Should().ContainSingle();
        world.Files.Count.Should().Be(1);
    }

    [Fact]
    public async Task Enforces_the_space_byte_limit_when_a_generated_image_exceeds_remaining_room()
    {
        var policy = DeploymentPolicy.SelfHosted with { MaxSpaceFileMegabytes = 1 };
        var generator = new FixedImageGenerator();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "image_create", """{"prompt":"A green barn","model":"picture-one"}""")],
            [new TextOutput("Space was full.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services =>
            {
                services.AddSingleton<IOptions<DeploymentPolicyOptions>>(
                    Options.Create(new DeploymentPolicyOptions { Rules = policy }));
                services.AddSingleton<IModelImageGenerator>(generator);
            });
        await SeedImageModel(world, "picture-one");
        await world.Transact(async services =>
        {
            var reserved = await services.GetRequiredService<FileRepository>().ReserveBytes(
                world.SpaceId,
                (1024L * 1024) - 10,
                1024L * 1024,
                RunWorld.Token);
            reserved.Should().BeTrue();
            services.GetRequiredService<DroverDbContext>().Files.Add(new StoredFile
            {
                Id = Guid.CreateVersion7(),
                StorageKey = FileStorageKeys.For(world.SpaceId, Guid.CreateVersion7()),
                Name = "existing.bin",
                ContentType = "application/octet-stream",
                Size = (1024L * 1024) - 10,
                Sha256 = "00",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            return true;
        });
        await world.Grant(ImageTools.CreateName, GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Create a barn picture.");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        generator.Requests.Should().ContainSingle().Which.MaxBytes.Should().Be(10);
        var toolCall = (await world.ReadToolCalls(runId)).Should().ContainSingle().Which;
        toolCall.IsError.Should().BeTrue();
        toolCall.Result!.Value.GetProperty("error").GetString().Should().Be(FileWords.SpaceFull(1));
        world.Files.Count.Should().Be(0, "the unreserved blob copy must be deleted when space reservation fails");
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(1);
    }

    [Fact]
    public async Task Refuses_non_png_bytes_returned_by_an_image_generator()
    {
        var generator = new FixedImageGenerator
        {
            ImageBytes = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61],
        };
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "image_create", """{"prompt":"A green barn","model":"picture-one"}""")],
            [new TextOutput("Not a PNG.")]);
        await using var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services => services.AddSingleton<IModelImageGenerator>(generator));
        await SeedImageModel(world, "picture-one");
        await world.Grant(ImageTools.CreateName, GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Create a barn picture.");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed, run.Error?.Message);
        var toolCall = (await world.ReadToolCalls(runId)).Should().ContainSingle().Which;
        toolCall.IsError.Should().BeTrue();
        toolCall.Result!.Value.GetProperty("error").GetString().Should().Contain("not a supported PNG");
        world.Files.Count.Should().Be(0);
        (await world.Read(db => db.Files.CountAsync(RunWorld.Token))).Should().Be(0);
    }

    #endregion

    #region Private Methods

    private static Task<bool> SeedImageModel(RunWorld world, string model) =>
        world.Read(db =>
        {
            db.ModelCatalog.Add(new ModelCatalogEntry
            {
                ModelConnectionId = world.ConnectionId,
                Model = model,
                DisplayName = model,
                Capabilities = new ModelCapabilities { ImageGeneration = true },
                State = AvailabilityState.Available,
            });
            return Task.FromResult(true);
        });

    private static BuiltinTool Tool(EngineHost host) =>
        host.Services.GetServices<BuiltinTool>().Single(tool => tool.Name == ImageTools.CreateName);

    private static BuiltinToolCall Call(RunWorld world, EngineHost host) =>
        new(
            JsonElement.Parse("""{"prompt":"A barn","model":"picture-one"}"""),
            world.BotId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            host.Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { });

    private static BuiltinToolCall RunCall(
        RunWorld world,
        EngineHost host,
        Guid threadId,
        Guid runId,
        Guid toolCallId) =>
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

    #endregion

    #region Nested Types

    private sealed class FixedImageGenerator : IModelImageGenerator
    {
        #region Properties

        public string Kind => ScriptedModelProvider.ConnectionKind;

        public Uri? FixedAddress { get; } = new("https://scripted-model.example.test/v1/");

        public ConcurrentQueue<ModelEndpoint> Endpoints { get; } = new();

        public ConcurrentQueue<EffectivePolicy?> Policies { get; } = new();

        public ConcurrentQueue<string?> Keys { get; } = new();

        public ConcurrentQueue<ModelImageRequest> Requests { get; } = new();

        public Func<CancellationToken, Task> BeforeReturn { get; init; } = _ => Task.CompletedTask;

        public byte[]? ImageBytes { get; init; }

        public ModelImageUsage Usage { get; init; } = new() { Cost = 0.07m };

        #endregion

        #region Interface Implementations

        public async Task<ModelImageResult> Generate(
            ModelEndpoint connection,
            string? apiKey,
            ModelImageRequest request,
            CancellationToken cancellationToken)
        {
            Endpoints.Enqueue(connection);
            Policies.Enqueue(OutboundAttempt.Current?.Policy);
            Keys.Enqueue(apiKey);
            Requests.Enqueue(request);
            await BeforeReturn(cancellationToken);
            return new ModelImageResult(ImageBytes ?? TestPng.Make(64, 32), "image/png", request.Model)
            {
                Usage = Usage,
            };
        }

        #endregion
    }

    #endregion
}
