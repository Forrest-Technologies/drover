namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// The sandbox broker and the four sandbox tools over PostgreSQL and the real run engine, against a provider in
/// memory. Each test is one thing the broker promises: one sandbox per group, the sharing rules, the token kept out of
/// everything a person or a model reads, the deployment's limits, and taint.
/// </summary>
public sealed class SandboxBrokerTests(PostgresServer postgres)
{
    #region Creating And Reusing

    [Fact]
    public async Task A_first_command_creates_one_sandbox_stores_its_token_in_the_vault_and_streams_its_output()
    {
        var provider = new FakeSandboxProvider();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "sandbox_run", """{"command":"echo hello"}""")],
            [new ToolCallOutput("c2", "sandbox_run", """{"command":"echo again"}""")],
            [new TextOutput("Done.")]);
        await using var world = await SandboxWorld(provider, script);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        await world.Grant("sandbox.run", GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Say hello twice");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        provider.Created.Should().Be(1, "the second command reuses the sandbox the first one made");
        var row = await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token));
        row.Should().Match<Sandbox>(sandbox => sandbox.OwnerBotId == world.BotId && sandbox.State == SandboxState.Running
            && sandbox.Provider == "fake" && sandbox.ProviderSandboxId == provider.Only().Id && sandbox.LastUsedAt != null);
        var secret = await world.Read(db => db.Secrets.AsNoTracking().SingleAsync(found => found.Id == row.AccessSecretId, RunWorld.Token));
        secret.Kind.Should().Be(SecretKind.SandboxToken);
        secret.AudienceUserId.Should().Be(Audience.Everyone, "every member's runs use the bot's sandbox");
        (await world.Transact(services => services.GetRequiredService<ISecretVault>().Reveal(secret.Id, RunWorld.Token)))
            .Should().Be(provider.Only().Token);

        var calls = await world.ReadToolCalls(runId);
        calls.Should().HaveCount(2).And.OnlyContain(call => call.SandboxId == row.Id && !call.IsError);
        calls.Single(call => call.Arguments.GetRawText().Contains("hello")).Result!.Value.GetProperty("stdout").GetString().Should().Be("echo hello\n");
        var events = await world.ReadEvents(runId);
        events.Where(stored => stored.Type == RunEventTypes.ToolOutput).Select(stored => RunWorld.Data<ToolOutput>(stored).Text)
            .Should().Equal("echo hello\n", "echo again\n");
        provider.Commands.Select(command => command.Program).Should().AllBe("/bin/bash");
    }

    [Fact]
    public async Task Two_first_uses_at_once_make_one_row_and_one_sandbox()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index =>
            broker.Run(Call(world, world.BotId), null, $"echo {index}", null, null, RunWorld.Token)));

        results.Should().OnlyContain(result => result.ExitCode == 0);
        provider.Created.Should().Be(1);
        (await world.Read(db => db.Sandboxes.CountAsync(RunWorld.Token))).Should().Be(1);
        (await world.Read(db => db.Secrets.CountAsync(secret => secret.Kind == SecretKind.SandboxToken, RunWorld.Token))).Should().Be(1);
    }

    [Fact]
    public async Task A_paused_sandbox_is_resumed_and_a_new_token_replaces_the_old_one_in_the_vault()
    {
        var provider = new FakeSandboxProvider { NewTokenOnResume = true };
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.WriteFile(Call(world, world.BotId), null, "/home/user/notes.txt", "kept", RunWorld.Token);
        var firstToken = provider.Only().Token;

        provider.PauseAll();
        var read = await broker.ReadFile(Call(world, world.BotId), null, "/home/user/notes.txt", RunWorld.Token);

        read.Content.Should().Be("kept");
        provider.Only().State.Should().Be(ProviderSandboxState.Running);
        provider.Only().Token.Should().NotBe(firstToken);
        var row = await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token));
        (await world.Transact(services => services.GetRequiredService<ISecretVault>().Reveal(row.AccessSecretId!.Value, RunWorld.Token)))
            .Should().Be(provider.Only().Token);
        provider.Created.Should().Be(1);
    }

    [Fact]
    public async Task A_sandbox_the_provider_lost_is_replaced_once_and_the_result_says_its_files_are_gone()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.Run(Call(world, world.BotId), null, "true", null, null, RunWorld.Token);

        provider.LoseAll();
        var result = await broker.Run(Call(world, world.BotId), null, "ls", null, null, RunWorld.Token);

        result.NewSandbox.Should().BeTrue();
        provider.Created.Should().Be(2);
        var rows = await world.Read(db => db.Sandboxes.AsNoTracking().OrderBy(sandbox => sandbox.Id).ToListAsync(RunWorld.Token));
        rows.Select(sandbox => sandbox.State).Should().Equal(SandboxState.Destroyed, SandboxState.Running);
        rows[0].AccessSecretId.Should().BeNull("the lost sandbox's token is deleted");
    }

    #endregion

    #region Sharing

    [Fact]
    public async Task A_bot_in_a_subtree_share_works_in_the_sandbox_of_the_highest_subtree_bot()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Subtree);
        var child = await AddBot(world, "child", world.BotId, SandboxPolicy.Private);
        var grandchild = await AddBot(world, "grandchild", child, SandboxPolicy.Subtree);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();

        await broker.WriteFile(Call(world, world.BotId), null, "/w/plan.txt", "from the top", RunWorld.Token);
        var fromChild = await broker.ReadFile(Call(world, child), null, "/w/plan.txt", RunWorld.Token);
        var fromGrandchild = await broker.ReadFile(Call(world, grandchild), null, "/w/plan.txt", RunWorld.Token);

        fromChild.Content.Should().Be("from the top");
        fromGrandchild.Content.Should().Be("from the top");
        provider.Created.Should().Be(1);
        (await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token))).OwnerBotId.Should().Be(world.BotId);
    }

    [Fact]
    public async Task A_sibling_cannot_work_in_another_bots_private_sandbox()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var first = await AddBot(world, "first", world.BotId, SandboxPolicy.Private);
        var second = await AddBot(world, "second", world.BotId, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.WriteFile(Call(world, first), null, "/w/secret.txt", "mine", RunWorld.Token);

        var sibling = () => broker.ReadFile(Call(world, second), "first", "/w/secret.txt", RunWorld.Token);
        var parent = () => broker.ReadFile(Call(world, world.BotId), "@first", "/w/secret.txt", RunWorld.Token);

        (await sibling.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("may not work in the sandbox of @first");
        await parent.Should().ThrowAsync<BuiltinToolException>("a parent reaches a child's sandbox only with sandbox.enter, which is not built");
        provider.Created.Should().Be(1);
    }

    [Fact]
    public async Task A_named_member_has_its_own_sandbox_and_reaches_the_shared_one_only_by_naming_it()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Named);
        var member = await AddBot(world, "member", null, SandboxPolicy.Private);
        var stranger = await AddBot(world, "stranger", null, SandboxPolicy.Private);
        await world.Read(db =>
        {
            db.BotSandboxShares.Add(new BotSandboxShare { BotId = world.BotId, SharedWithBotId = member });
            return Task.FromResult(true);
        });
        var ownerHandle = await world.Read(db => db.Bots.Where(bot => bot.Id == world.BotId).Select(bot => bot.Handle).SingleAsync(RunWorld.Token));
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.WriteFile(Call(world, world.BotId), null, "/w/shared.txt", "ours", RunWorld.Token);

        var own = () => broker.ReadFile(Call(world, member), null, "/w/shared.txt", RunWorld.Token);
        var named = await broker.ReadFile(Call(world, member), ownerHandle, "/w/shared.txt", RunWorld.Token);
        var refused = () => broker.ReadFile(Call(world, stranger), ownerHandle, "/w/shared.txt", RunWorld.Token);

        (await own.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("There is no file", "the member's own sandbox is a different one");
        named.Content.Should().Be("ours");
        await refused.Should().ThrowAsync<BuiltinToolException>();
        (await world.Read(db => db.Sandboxes.AsNoTracking().Select(sandbox => sandbox.OwnerBotId).ToListAsync(RunWorld.Token)))
            .Should().BeEquivalentTo([world.BotId, member]);
    }

    #endregion

    #region Secrets

    [Fact]
    public async Task The_token_never_reaches_an_event_a_tool_call_or_the_model_even_when_the_sandbox_prints_it()
    {
        var provider = new FakeSandboxProvider();
        provider.Behaviour = (_, token, _) => PrintSplit($"token={token}\n", token.Length / 2);
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "sandbox_run", """{"command":"cat /proc/self/environ"}""")],
            [new TextOutput("Done.")]);
        await using var world = await SandboxWorld(provider, script);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        await world.Grant("sandbox.run", GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Print it");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var token = provider.Only().Token;
        var events = await world.ReadEvents(runId);
        events.Should().OnlyContain(stored => !stored.Data.Contains(token));
        string.Concat(events.Where(stored => stored.Type == RunEventTypes.ToolOutput).Select(stored => RunWorld.Data<ToolOutput>(stored).Text))
            .Should().Be($"token={Redactor.Mark}\n", "a token split across two pieces is caught too");
        (await world.ReadToolCalls(runId)).Should().OnlyContain(call =>
            !call.Arguments.GetRawText().Contains(token) && !call.Result!.Value.GetRawText().Contains(token));
        script.Requests[1].Should().OnlyContain(message => !message.Summary.Contains(token));
        (await world.ReadMessages(await world.Read(db => db.Runs.Where(run => run.Id == runId).Select(run => run.ThreadId).SingleAsync(RunWorld.Token))))
            .Should().OnlyContain(message => !JsonSerializer.Serialize(message.Parts).Contains(token));
    }

    #endregion

    #region Policy And Limits

    [Fact]
    public async Task A_bot_whose_sandbox_policy_is_none_is_not_offered_the_sandbox_tools_whatever_its_grants()
    {
        var provider = new FakeSandboxProvider();
        var script = new ScriptedModel([new ToolCallOutput("c1", "sandbox_run", """{"command":"id"}""")], [new TextOutput("No.")]);
        await using var world = await SandboxWorld(provider, script);
        await world.Grant("sandbox.run", GrantEffect.Allow);
        await world.Grant("sandbox.read_file", GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Run it");
        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        world.Model.ToolsOffered[0].Should().NotContain(name => name.StartsWith("sandbox_", StringComparison.Ordinal));
        provider.Created.Should().Be(0);
    }

    [Fact]
    public async Task A_bot_with_a_sandbox_is_offered_only_the_sandbox_tools_it_was_granted()
    {
        var provider = new FakeSandboxProvider();
        var script = new ScriptedModel([new TextOutput("Hello.")]);
        await using var world = await SandboxWorld(provider, script);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        await world.Grant("sandbox.list_files", GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Hi");
        await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        world.Model.ToolsOffered[0].Should().BeEquivalentTo(["sandbox_list_files", "time_now"], "a new bot is granted no sandbox tool by default");
    }

    [Fact]
    public async Task A_command_past_the_deployments_time_limit_is_stopped_and_the_result_says_so()
    {
        var provider = new FakeSandboxProvider { Behaviour = (command, _, cancellationToken) => RunUntilTimeLimit(command, cancellationToken) };
        await using var world = await SandboxWorld(provider, policy: DeploymentPolicy.SelfHosted with { MaxSandboxCommandSeconds = 1 });
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();

        var result = await broker.Run(Call(world, world.BotId), null, "sleep 600", null, TimeSpan.FromHours(2), RunWorld.Token);

        provider.Commands.Single().Timeout.Should().Be(TimeSpan.FromSeconds(1), "the bot's own limit cannot exceed the deployment's");
        result.TimedOut.Should().BeTrue();
        result.ExitCode.Should().BeNull();
        result.Stdout.Should().Be("started\n");
    }

    [Fact]
    public async Task A_file_past_the_deployments_size_limit_is_neither_written_nor_read()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider, policy: DeploymentPolicy.SelfHosted with { MaxSandboxFileBytes = 10 });
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.WriteFile(Call(world, world.BotId), null, "/w/small.txt", "0123456789", RunWorld.Token);
        provider.Only().Files["/w/big.txt"] = Encoding.UTF8.GetBytes("01234567890");

        var write = () => broker.WriteFile(Call(world, world.BotId), null, "/w/big.txt", "01234567890", RunWorld.Token);
        var read = () => broker.ReadFile(Call(world, world.BotId), null, "/w/big.txt", RunWorld.Token);

        (await write.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("at most 10 bytes");
        (await read.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain("larger than 10 bytes");
        (await broker.ReadFile(Call(world, world.BotId), null, "/w/small.txt", RunWorld.Token)).Content.Should().Be("0123456789");
    }

    [Fact]
    public async Task Open_network_is_refused_where_the_deployment_forbids_it_and_the_idle_time_is_capped()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider, policy: DeploymentPolicy.Hosted);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private, NetworkAccess.Open);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();

        var open = () => broker.Run(Call(world, world.BotId), null, "curl example.com", null, null, RunWorld.Token);

        (await open.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain(nameof(DeploymentPolicy.AllowOpenSandboxNetwork));
        provider.Created.Should().Be(0);

        await GiveSandbox(world, world.BotId, SandboxPolicy.Private, NetworkAccess.Allowlist, idleMinutes: 600);
        await broker.WriteFile(Call(world, world.BotId), null, "/w/a.txt", "a", RunWorld.Token);
        provider.Specs.Single().IdleTimeout.Should().Be(TimeSpan.FromMinutes(DeploymentPolicy.HostedMaxSandboxIdleMinutes));
        provider.Specs.Single().Network.Access.Should().Be(NetworkAccess.Allowlist);
        provider.Specs.Single().Network.AllowedHosts.Should().Equal("example.com");
    }

    [Fact]
    public async Task A_changed_network_setting_reaches_the_running_sandbox_on_its_next_use()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private, NetworkAccess.Open);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.Run(Call(world, world.BotId), null, "true", null, null, RunWorld.Token);
        await broker.Run(Call(world, world.BotId), null, "true", null, null, RunWorld.Token);

        await GiveSandbox(world, world.BotId, SandboxPolicy.Private, NetworkAccess.Off);
        await broker.Run(Call(world, world.BotId), null, "true", null, null, RunWorld.Token);

        provider.NetworksSet.Select(network => network.Access).Should().Equal([NetworkAccess.Off], "the rules are set again only when they changed");
    }

    [Fact]
    public async Task A_provider_that_finds_another_groups_record_gives_the_model_an_error_and_runs_nothing()
    {
        var provider = new FakeSandboxProvider();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "sandbox_run", """{"command":"echo one"}""")],
            [new ToolCallOutput("c2", "sandbox_run", """{"command":"echo two"}""")],
            [new TextOutput("It refused.")]);
        await using var world = await SandboxWorld(provider, script);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        await world.Grant("sandbox.run", GrantEffect.Allow);
        world.Model.BeforeCall = step =>
        {
            // The first command runs before the second model call; from then on the provider's record disagrees.
            provider.MetadataMismatch = step >= 1;
            return Task.CompletedTask;
        };
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Run twice");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var calls = (await world.ReadToolCalls(runId)).OrderBy(call => call.StartedAt).ToList();
        calls[0].IsError.Should().BeFalse();
        calls[1].IsError.Should().BeTrue();
        calls[1].Result!.Value.GetProperty("error").GetString().Should().Contain("does not match this bot");
        provider.Commands.Should().ContainSingle("the second command never reached the sandbox");
    }

    #endregion

    #region Taint

    [Theory]
    [InlineData(NetworkAccess.Off, false)]
    [InlineData(NetworkAccess.Allowlist, true)]
    [InlineData(NetworkAccess.Open, true)]
    public async Task Output_read_from_a_sandbox_that_can_reach_the_network_taints_the_run(NetworkAccess network, bool tainted)
    {
        var provider = new FakeSandboxProvider();
        var script = new ScriptedModel([new ToolCallOutput("c1", "sandbox_run", """{"command":"curl example.com"}""")], [new TextOutput("Read it.")]);
        await using var world = await SandboxWorld(provider, script);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private, network);
        await world.Grant("sandbox.run", GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Fetch it");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed);
        run.Tainted.Should().Be(tainted);
    }

    #endregion

    #region Helpers

    private async Task<RunWorld> SandboxWorld(FakeSandboxProvider provider, ScriptedModel? script = null, DeploymentPolicy? policy = null) =>
        await RunWorld.Create(
            postgres,
            script ?? new ScriptedModel([new TextOutput("Done.")]),
            everyHost: services =>
            {
                services.AddSingleton<ISandboxProvider>(provider);
                services.AddSandboxTools();

                if (policy is not null)
                {
                    services.AddSingleton<IOptions<DeploymentPolicyOptions>>(Options.Create(new DeploymentPolicyOptions { Rules = policy }));
                }
            });

    /// <summary>Sets the bot's sandbox as a person would in the editor. A named-hosts sandbox may reach example.com.</summary>
    private static Task<bool> GiveSandbox(RunWorld world, Guid botId, SandboxPolicy policy, NetworkAccess network = NetworkAccess.Off, int? idleMinutes = null) =>
        world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == botId, RunWorld.Token);
            bot.SandboxPolicy = policy;
            bot.SandboxSettings = new SandboxSettings
            {
                Network = network,
                AllowedHosts = network is NetworkAccess.Allowlist ? ["example.com"] : [],
                IdleTimeoutMinutes = idleMinutes,
            };
            return true;
        });

    /// <summary>A bot under <paramref name="parentId"/>, or at the top, with its closure rows, as the tree keeps them.</summary>
    private static Task<Guid> AddBot(RunWorld world, string handle, Guid? parentId, SandboxPolicy policy) =>
        world.Read(async db =>
        {
            var above = parentId is { } parent
                ? await db.BotAncestors.AsNoTracking().Where(row => row.DescendantBotId == parent).ToListAsync(RunWorld.Token)
                : [];
            var bot = new Bot
            {
                Name = handle,
                Handle = handle,
                ModelConnectionId = world.ConnectionId,
                Model = "scripted",
                ParentBotId = parentId,
                Depth = above.Count,
                SandboxPolicy = policy,
            };
            db.Bots.Add(bot);
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = bot.Id, DescendantBotId = bot.Id, Distance = 0 });

            foreach (var row in above)
            {
                db.BotAncestors.Add(new BotAncestor { AncestorBotId = row.AncestorBotId, DescendantBotId = bot.Id, Distance = row.Distance + 1 });
            }

            return bot.Id;
        });

    /// <summary>A tool call of the bot in a run of the world's person, made directly, outside any model step.</summary>
    private static BuiltinToolCall Call(RunWorld world, Guid botId) =>
        new(
            JsonElement.Parse("{}"),
            botId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            world.Hosts[^1].Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask);

    private static async IAsyncEnumerable<CommandOutput> PrintSplit(string text, int at)
    {
        await Task.Yield();
        yield return new CommandStarted(1);
        yield return new CommandStdout(Encoding.UTF8.GetBytes(text[..at]));
        yield return new CommandStdout(Encoding.UTF8.GetBytes(text[at..]));
        yield return new CommandExited(0, null);
    }

    /// <summary>A command that never ends by itself: the provider stops it at its time limit, as a real one does.</summary>
    private static async IAsyncEnumerable<CommandOutput> RunUntilTimeLimit(CommandRequest command, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new CommandStarted(1);
        yield return new CommandStdout(Encoding.UTF8.GetBytes("started\n"));
        await Task.Delay(command.Timeout, cancellationToken);
        throw new SandboxProviderException(SandboxFailure.TimedOut, "The command ran past its time limit.");
    }

    #endregion
}
