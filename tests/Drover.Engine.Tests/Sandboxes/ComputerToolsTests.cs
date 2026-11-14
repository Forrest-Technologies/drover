namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// The computer tools over PostgreSQL and the real run engine, against a provider in memory: a screenshot reaches the
/// model's next call as a picture and is stored nowhere, the tools are offered only where a screen, a model that sees,
/// and the deployment allow them, a screenshot taints like any read, and a change of screen replaces the sandbox.
/// </summary>
public sealed class ComputerToolsTests(PostgresServer postgres)
{
    #region Private Fields

    private static readonly byte[] png = TestPng.Make(1024, 768);

    #endregion

    #region The Picture Reaches The Model Once

    [Fact]
    public async Task A_screenshot_reaches_the_next_model_call_as_a_picture_and_only_its_small_result_is_stored()
    {
        var provider = DesktopProvider();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "computer_screenshot")],
            [new ToolCallOutput("c2", "computer_click", """{"x":10,"y":20,"double":true}""")],
            [new TextOutput("Clicked.")]);
        await using var world = await ComputerWorld(provider, script);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Look and click");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed);
        run.Tainted.Should().BeFalse("the sandbox cannot reach the network");
        world.Model.ImagesSeen.Select(images => images.Count).Should().Equal(0, 1, 0);
        var shown = world.Model.ImagesSeen[1].Single();
        shown.MediaType.Should().Be("image/png");
        shown.Data.ToArray().Should().Equal(png);
        var seen = world.Script.Requests[1];
        seen.TakeLast(2).Should().Equal(
            new SeenMessage("user", "The image from the computer_screenshot call c1. It is shown once and not kept."),
            new SeenMessage("user", "image image/png"));
        var result = seen.Single(message => message.Role == "tool").Summary;
        JsonElement.DeepEquals(JsonElement.Parse(result["c1 = ".Length..]), JsonElement.Parse("""{"screenshot":"attached","width":1024,"height":768}""")).Should().BeTrue(result);

        var calls = (await world.ReadToolCalls(runId)).OrderBy(call => call.StartedAt).ToList();
        calls.Should().OnlyContain(call => !call.IsError);
        JsonElement.DeepEquals(calls[0].Result!.Value, JsonElement.Parse("""{"screenshot":"attached","width":1024,"height":768}""")).Should().BeTrue();
        var stored = await world.ReadMessages(run.ThreadId);
        stored.SelectMany(message => message.Parts).Should().NotContain(part => part is ImagePart);
        var picture = Convert.ToBase64String(png);
        (await world.ReadEvents(runId)).Should().OnlyContain(written => !written.Data.Contains(picture));
        stored.Should().OnlyContain(message => !JsonSerializer.Serialize(message.Parts, JsonSerializerOptions.Default).Contains(picture));
    }

    [Fact]
    public async Task Every_action_is_a_program_with_its_arguments_on_a_sandbox_made_from_the_desktop_image()
    {
        var provider = DesktopProvider();
        var script = new ScriptedModel(
            [new ToolCallOutput("c1", "computer_type", """{"text":"x; rm -rf ~ && $(reboot)"}""")],
            [new ToolCallOutput("c2", "computer_key", """{"keys":["ctrl","l"]}""")],
            [new ToolCallOutput("c3", "computer_scroll", """{"direction":"up","amount":4}""")],
            [new TextOutput("Done.")]);
        await using var world = await ComputerWorld(provider, script);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Type, press, and scroll");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var spec = provider.Specs.Single();
        spec.Desktop.Should().BeTrue();
        spec.Environment.Should().Contain("DISPLAY", ":0");
        var actions = provider.Commands.Where(command => command.Program != "/bin/bash").ToList();
        actions.Select(command => string.Join(' ', [command.Program, .. command.Arguments])).Should().Equal(
            "xdotool type --delay 12 -- x; rm -rf ~ && $(reboot)",
            "xdotool key Control_L+l",
            "xdotool click --repeat 4 4");
        var startDisplay = DesktopCommands.StartDisplay().Arguments;
        provider.Commands.Where(command => command.Program == "/bin/bash").Select(command => command.Arguments)
            .Should().HaveCount(3, "the display is checked before each action")
            .And.AllSatisfy(arguments => arguments.Should().Equal(startDisplay));
    }

    [Fact]
    public async Task A_display_that_will_not_start_is_an_error_for_the_model_and_no_action_runs()
    {
        var provider = DesktopProvider(displayExitCode: 4);
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_click", """{"x":1,"y":1}""")], [new TextOutput("It failed.")]);
        await using var world = await ComputerWorld(provider, script);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Click");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var call = (await world.ReadToolCalls(runId)).Single();
        call.IsError.Should().BeTrue();
        call.Result!.Value.GetProperty("error").GetString().Should().Contain("screen did not start");
        provider.Commands.Should().NotContain(command => command.Program == "xdotool");
    }

    [Fact]
    public async Task A_point_off_the_screen_is_refused_before_the_sandbox_is_touched()
    {
        var provider = DesktopProvider();
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_click", """{"x":1024,"y":10}""")], [new TextOutput("Off.")]);
        await using var world = await ComputerWorld(provider, script);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Click off the edge");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        (await world.ReadToolCalls(runId)).Single().Result!.Value.GetProperty("error").GetString().Should().Contain("off the screen");
        provider.Created.Should().Be(0);
    }

    #endregion

    #region Taint

    [Theory]
    [InlineData(NetworkAccess.Off, false)]
    [InlineData(NetworkAccess.Allowlist, true)]
    public async Task A_screenshot_of_a_sandbox_that_can_reach_the_network_taints_the_run(NetworkAccess network, bool tainted)
    {
        var provider = DesktopProvider();
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_screenshot")], [new TextOutput("Seen.")]);
        await using var world = await ComputerWorld(provider, script, network: network);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Look");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Status.Should().Be(RunStatus.Completed);
        run.Tainted.Should().Be(tainted);
    }

    [Fact]
    public async Task An_action_reads_nothing_back_and_does_not_taint()
    {
        var provider = DesktopProvider();
        var script = new ScriptedModel([new ToolCallOutput("c1", "computer_key", """{"keys":["enter"]}""")], [new TextOutput("Pressed.")]);
        await using var world = await ComputerWorld(provider, script, network: NetworkAccess.Open);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Press enter");
        var run = await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed);

        run.Tainted.Should().BeFalse();
        JsonElement.DeepEquals((await world.ReadToolCalls(runId)).Single().Result!.Value, JsonElement.Parse("""{"done":true}""")).Should().BeTrue();
    }

    #endregion

    #region Offered Only Where It Can Work

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    public async Task The_computer_tools_are_offered_only_with_a_screen_a_model_that_sees_and_a_deployment_that_allows_them(
        bool desktop,
        bool vision,
        bool allowed,
        bool offered)
    {
        var provider = DesktopProvider();
        var policy = DeploymentPolicy.SelfHosted with { AllowDesktopSandboxes = allowed };
        await using var world = await ComputerWorld(provider, new ScriptedModel([new TextOutput("Hi.")]), desktop: desktop, vision: vision, policy: policy);
        await world.Grant("sandbox.run", GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Hello");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var tools = world.Model.ToolsOffered.Single();
        tools.Should().Contain("sandbox_run");
        tools.Where(name => name.StartsWith("computer_", StringComparison.Ordinal)).Should().HaveCount(offered ? ComputerTools.Names.Count : 0);
    }

    [Fact]
    public async Task No_new_bot_is_granted_a_computer_tool()
    {
        await using var world = await ComputerWorld(DesktopProvider(), new ScriptedModel([new TextOutput("Hi.")]), grant: false);

        var granted = await world.Read(db => db.ToolGrants.AsNoTracking().Where(grant => grant.BotId == world.BotId).Join(db.Tools, grant => grant.ToolId, tool => tool.Id, (_, tool) => tool.Name).ToListAsync(RunWorld.Token));

        granted.Should().NotContain(name => ComputerTools.IsComputerTool(name));
        BuiltinTools.GrantedToNewBots.Should().NotContain(name => ComputerTools.IsComputerTool(name));
    }

    [Fact]
    public async Task A_bot_set_to_have_a_screen_where_the_deployment_forbids_one_is_refused_at_save_and_at_use()
    {
        var provider = DesktopProvider();
        var policy = DeploymentPolicy.SelfHosted with { AllowDesktopSandboxes = false };
        await using var world = await ComputerWorld(provider, new ScriptedModel([new TextOutput("Hi.")]), desktop: false, policy: policy);
        var host = world.StartHost();

        var change = await SetDesktop(world, true);

        change.Problem.Should().Contain("does not offer sandboxes with a screen");
        await GiveScreen(world, desktop: true);
        var use = () => host.Services.GetRequiredService<SandboxBroker>().Run(Call(world), null, "true", null, null, RunWorld.Token);
        (await use.Should().ThrowAsync<BuiltinToolException>()).Which.Message.Should().Contain(nameof(DeploymentPolicy.AllowDesktopSandboxes));
        provider.Created.Should().Be(0);
    }

    #endregion

    #region Changing The Screen

    [Fact]
    public async Task Turning_the_screen_on_retires_the_sandbox_and_the_next_use_makes_one_with_a_screen_and_destroys_the_old()
    {
        var provider = DesktopProvider();
        await using var world = await ComputerWorld(provider, new ScriptedModel([new TextOutput("Hi.")]), desktop: false);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.WriteFile(Call(world), null, "/home/user/notes.txt", "kept", RunWorld.Token);
        var old = provider.Specs.Single();

        (await SetDesktop(world, true)).Problem.Should().BeNull();
        var retired = await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token));
        retired.State.Should().Be(SandboxState.Retiring);

        var shot = await broker.Screenshot(Call(world), RunWorld.Token);

        shot.NewSandbox.Should().BeFalse("the sandbox was replaced on purpose, not lost");
        provider.Specs.Select(spec => spec.Desktop).Should().Equal(false, true);
        provider.Specs.Last().IdempotencyKey.Should().NotBe(old.IdempotencyKey);
        var rows = await world.Read(db => db.Sandboxes.AsNoTracking().OrderBy(sandbox => sandbox.Id).ToListAsync(RunWorld.Token));
        rows.Select(row => row.State).Should().Equal(SandboxState.Destroyed, SandboxState.Running);
        rows[0].AccessSecretId.Should().BeNull("the old sandbox's token is deleted with it");
        (await world.Read(db => db.Secrets.CountAsync(secret => secret.Kind == SecretKind.SandboxToken, RunWorld.Token))).Should().Be(1);

        (await SetDesktop(world, true)).Problem.Should().BeNull();
        (await world.Read(db => db.Sandboxes.CountAsync(sandbox => sandbox.State == SandboxState.Running, RunWorld.Token)))
            .Should().Be(1, "saving the bot without changing its screen keeps its sandbox");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// A provider whose sandbox behaves like a desktop: the display check exits with <paramref name="displayExitCode"/>,
    /// scrot writes a PNG to the path it is given, rm removes it, and xdotool does what it is told.
    /// </summary>
    private static FakeSandboxProvider DesktopProvider(int displayExitCode = 0)
    {
        var provider = new FakeSandboxProvider();
        provider.Behaviour = (command, token, cancellationToken) => Desktop(provider.WithToken(token), command, displayExitCode, cancellationToken);
        return provider;
    }

    private static async IAsyncEnumerable<CommandOutput> Desktop(
        FakeSandboxProvider.FakeSandbox sandbox,
        CommandRequest command,
        int displayExitCode,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        switch (command.Program)
        {
            case "scrot":
                sandbox.Files[command.Arguments[^1]] = png;
                break;
            case "rm":
                sandbox.Files.TryRemove(command.Arguments[^1], out _);
                break;
            default:
                break;
        }

        yield return new CommandStarted(1);
        yield return new CommandExited(command.Program == "/bin/bash" ? displayExitCode : 0, null);
    }

    private async Task<RunWorld> ComputerWorld(
        FakeSandboxProvider provider,
        ScriptedModel script,
        bool desktop = true,
        bool vision = true,
        NetworkAccess network = NetworkAccess.Off,
        DeploymentPolicy? policy = null,
        bool grant = true)
    {
        var world = await RunWorld.Create(
            postgres,
            script,
            everyHost: services =>
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
            bot.SandboxSettings = new SandboxSettings
            {
                Network = network,
                AllowedHosts = network is NetworkAccess.Allowlist ? ["example.com"] : [],
                Desktop = desktop,
            };
            var model = await db.ModelCatalog.SingleAsync(RunWorld.Token);
            model.Capabilities = new ModelCapabilities { Vision = vision };
            return true;
        });

        if (grant)
        {
            foreach (var tool in ComputerTools.Names)
            {
                await world.Grant(tool, GrantEffect.Allow);
            }
        }

        return world;
    }

    private static Task<bool> GiveScreen(RunWorld world, bool desktop) =>
        world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxSettings = bot.SandboxSettings with { Desktop = desktop };
            return true;
        });

    /// <summary>Saves the bot with its screen set, through the engine's own service, as the editor does.</summary>
    private static Task<BotChange> SetDesktop(RunWorld world, bool desktop) =>
        world.Transact(async services =>
        {
            var bot = (await services.GetRequiredService<BotRepository>().FindBot(world.BotId, RunWorld.Token))!;
            var edit = new BotEdit(
                bot.Name,
                bot.Handle,
                bot.Avatar,
                bot.DescriptionForBots,
                bot.Instructions,
                bot.ModelConnectionId,
                bot.Model,
                bot.ModelSettings,
                bot.MemorySettings,
                bot.SandboxPolicy,
                bot.SandboxSettings with { Desktop = desktop },
                [],
                bot.BuildLimits);
            return await services.GetRequiredService<BotService>().Update(world.BotId, edit, world.UserId, RunWorld.Token);
        });

    /// <summary>A tool call of the world's bot, made directly, outside any model step.</summary>
    private static BuiltinToolCall Call(RunWorld world) =>
        new(
            JsonElement.Parse("{}"),
            world.BotId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            world.Hosts[^1].Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { });

    #endregion
}
