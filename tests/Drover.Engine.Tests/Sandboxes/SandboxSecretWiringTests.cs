namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// Sandbox secrets as a new sandbox gets them, against the fake provider and real PostgreSQL: a brokered value enters
/// only as a placeholder, with the broker's address, a token, and the space's authority, and the sandbox may reach
/// the broker alone; a raw value enters as it is; the narrowest value of a name wins; and the broker's decisions,
/// made as each request arrives, follow the secrets as they are now. Every value is generated for the test.
/// </summary>
public sealed class SandboxSecretWiringTests(PostgresServer postgres)
{
    #region Private Fields

    private const string BrokerHost = "broker.example.test";

    #endregion

    #region Sandbox Start

    [Fact]
    public async Task A_brokered_value_enters_the_sandbox_only_as_a_placeholder_with_the_broker_and_its_authority()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SecretWorld(provider);
        var value = NewValue();
        await AddSecret(world, Brokered("DEPLOY_TOKEN", value, "bots", [world.BotId]));
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();

        await broker.Run(Call(world, world.BotId), null, "echo hi", null, null, RunWorld.Token);

        var spec = provider.Specs.Single();
        var placeholder = spec.Environment["DEPLOY_TOKEN"];
        placeholder.Should().StartWith(BrokerTokens.PlaceholderPrefix).And.NotContain(value);
        spec.Environment.Values.Should().NotContain(found => found.Contains(value, StringComparison.Ordinal));
        spec.Environment["HTTPS_PROXY"].Should().StartWith($"https://{BrokerTokens.ProxyUser}:").And.EndWith($"@{BrokerHost}:8443");
        spec.Environment["SSL_CERT_FILE"].Should().Be(SandboxSecretsAtStart.BundlePath);
        spec.Network.Access.Should().Be(NetworkAccess.Allowlist);
        spec.Network.AllowedHosts.Should().Equal([BrokerHost], "it may reach the broker and nothing else");
        provider.Only().Files[SandboxSecretsAtStart.AuthorityPath].Should().NotBeEmpty();
        Encoding.UTF8.GetString(provider.Only().Files[SandboxSecretsAtStart.AuthorityPath]).Should().StartWith("-----BEGIN CERTIFICATE-----");
        provider.Commands.First().Arguments.Should().Equal("-c", SandboxSecretsAtStart.BundleScript);

        var row = await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token));
        var token = TokenOf(spec);
        row.BrokerTokenHash.Should().Equal(BrokerTokens.Hash(token));
        var entry = await world.Read(db => db.SandboxSecretEntries.AsNoTracking().SingleAsync(RunWorld.Token));
        entry.PlaceholderHash.Should().Equal(BrokerTokens.Hash(placeholder));
        (await world.Read(db => db.Secrets.CountAsync(secret => secret.Kind == SecretKind.BrokerAuthority, RunWorld.Token))).Should().Be(1);
    }

    [Fact]
    public async Task A_raw_value_enters_as_it_is_and_without_the_broker_a_brokered_one_does_not_enter_at_all()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SecretWorld(provider, brokerSetUp: false);
        var raw = NewValue();
        await AddSecret(world, Raw("SIGNING_KEY", raw));
        await AddSecret(world, Brokered("DEPLOY_TOKEN", NewValue(), "space"));
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();

        await broker.Run(Call(world, world.BotId), null, "echo hi", null, null, RunWorld.Token);

        var spec = provider.Specs.Single();
        spec.Environment["SIGNING_KEY"].Should().Be(raw);
        spec.Environment.Should().NotContainKey("DEPLOY_TOKEN").And.NotContainKey("HTTPS_PROXY");
        spec.Network.Access.Should().Be(NetworkAccess.Off, "without the broker the bot's own setting is what the provider is told");
        (await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token))).BrokerTokenHash.Should().BeNull();
    }

    [Fact]
    public async Task A_bots_own_value_wins_over_the_spaces_and_another_bots_value_is_not_given()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SecretWorld(provider, brokerSetUp: false);
        var other = await AddBot(world, "other");
        var forBot = NewValue();
        await AddSecret(world, Raw("API_KEY", NewValue()));
        await AddSecret(world, Raw("API_KEY", forBot) with { Scope = "bots", BotIds = [world.BotId] });
        await AddSecret(world, Raw("OTHER_KEY", NewValue()) with { Scope = "bots", BotIds = [other] });
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();

        await broker.Run(Call(world, world.BotId), null, "echo hi", null, null, RunWorld.Token);

        provider.Specs.Single().Environment["API_KEY"].Should().Be(forBot);
        provider.Specs.Single().Environment.Should().NotContainKey("OTHER_KEY");
    }

    [Fact]
    public async Task Removing_a_raw_value_takes_every_sandbox_that_holds_it_out_of_use_and_the_next_one_starts_without_it()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SecretWorld(provider, brokerSetUp: false);
        var secret = await AddSecret(world, Raw("SIGNING_KEY", NewValue()));
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.Run(Call(world, world.BotId), null, "echo hi", null, null, RunWorld.Token);

        (await world.Transact(services => services.GetRequiredService<SandboxSecretService>().Delete(secret.Id, world.UserId, RunWorld.Token))).Should().BeTrue();

        (await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(RunWorld.Token))).State.Should().Be(SandboxState.Retiring);
        await broker.Run(Call(world, world.BotId), null, "echo again", null, null, RunWorld.Token);
        provider.Specs.Should().HaveCount(2);
        provider.Specs.Last().Environment.Should().NotContainKey("SIGNING_KEY");
    }

    [Fact]
    public async Task A_raw_value_removed_while_its_sandbox_is_being_made_does_not_survive_in_it()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SecretWorld(provider, brokerSetUp: false);
        var secret = await AddSecret(world, Raw("SIGNING_KEY", NewValue()));
        provider.BeforeCreate = async (_, _) =>
        {
            provider.BeforeCreate = null;
            (await world.Transact(services => services.GetRequiredService<SandboxSecretService>().Delete(secret.Id, world.UserId, RunWorld.Token))).Should().BeTrue();
        };
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();

        await broker.Run(Call(world, world.BotId), null, "echo hi", null, null, RunWorld.Token);

        var rows = await world.Read(db => db.Sandboxes.AsNoTracking().OrderBy(row => row.Id).ToListAsync(RunWorld.Token));
        rows[0].State.Should().BeOneOf([SandboxState.Retiring, SandboxState.Destroyed], "the sandbox made with the removed value is taken out of use");
        await broker.Run(Call(world, world.BotId), null, "echo again", null, null, RunWorld.Token);
        provider.Specs.Last().Environment.Should().NotContainKey("SIGNING_KEY");
    }

    [Fact]
    public async Task The_model_is_told_a_secrets_name_and_hosts_and_never_its_value_or_placeholder()
    {
        var provider = new FakeSandboxProvider();
        var script = new ScriptedModel([new ToolCallOutput("c1", "sandbox_run", """{"command":"env"}""")], [new TextOutput("Done.")]);
        await using var world = await SecretWorld(provider, script: script);
        var value = NewValue();
        await AddSecret(world, Brokered("DEPLOY_TOKEN", value, "bots", [world.BotId]));
        await world.Grant("sandbox.run", GrantEffect.Allow);
        world.StartHost();

        var runId = await world.Post(await world.NewThread(), "Deploy it");
        (await world.WaitForStatus(runId, RunStatus.Completed, RunStatus.Failed)).Status.Should().Be(RunStatus.Completed);

        var described = world.Model.Requests[0].Options.Tools!.Single(tool => tool.Name == "sandbox_run").Description;
        described.Should().Contain("DEPLOY_TOKEN (works only over HTTPS to api.example.test)");
        var placeholder = provider.Specs.Single().Environment["DEPLOY_TOKEN"];
        var everything = string.Join("\n", world.Model.Requests.Select(request => JsonSerializer.Serialize(request)));
        everything.Should().NotContain(value).And.NotContain(placeholder);
    }

    #endregion

    #region Broker Decisions

    [Fact]
    public async Task The_broker_knows_the_sandbox_by_its_token_and_swaps_a_placeholder_only_for_a_bound_host()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SecretWorld(provider);
        var value = NewValue();
        var secret = await AddSecret(world, Brokered("DEPLOY_TOKEN", value, "space") with { Hosts = ["api.example.test"] });
        var host = world.StartHost();
        await host.Services.GetRequiredService<SandboxBroker>().Run(Call(world, world.BotId), null, "echo hi", null, null, RunWorld.Token);
        var spec = provider.Specs.Single();
        var placeholder = spec.Environment["DEPLOY_TOKEN"];
        var service = host.Services.GetRequiredService<CredentialBrokerService>();

        var session = await service.Authenticate(TokenOf(spec), RunWorld.Token);
        var stranger = await service.Authenticate(BrokerTokens.NewToken(), RunWorld.Token);
        var bound = await service.Substitute(session!, "api.example.test", [(placeholder, BrokerPlacement.Header)], RunWorld.Token);
        var elsewhere = await service.Substitute(session!, "collector.example.test", [(placeholder, BrokerPlacement.Header)], RunWorld.Token);
        var inQuery = await service.Substitute(session!, "api.example.test", [(placeholder, BrokerPlacement.Query)], RunWorld.Token);
        var foreign = await service.Substitute(session!, "api.example.test", [(BrokerTokens.NewPlaceholder(), BrokerPlacement.Header)], RunWorld.Token);

        session!.IsBound("api.example.test").Should().BeTrue();
        session.IsBound("collector.example.test").Should().BeFalse();
        session.MayReach("collector.example.test").Should().BeFalse("the bot's network is off; only the bound host is let through");
        stranger.Should().BeNull();
        bound.Values[placeholder].Should().Be(value);
        elsewhere.Refusal.Should().Be("DEPLOY_TOKEN may not be sent to collector.example.test.");
        inQuery.Refusal.Should().Be("DEPLOY_TOKEN may only be sent in a request header.");
        foreign.Refusal.Should().NotBeNull();
        (await world.Read(db => db.AuditLog.AsNoTracking().Where(entry => entry.Action == "sandbox_secret.used").ToListAsync(RunWorld.Token)))
            .Should().ContainSingle().Which.Data.GetRawText().Should().NotContain(value);

        await world.Transact(services => services.GetRequiredService<SandboxSecretService>().Delete(secret.Id, world.UserId, RunWorld.Token));
        var afterRemoval = await service.Substitute(session, "api.example.test", [(placeholder, BrokerPlacement.Header)], RunWorld.Token);
        afterRemoval.Refusal.Should().NotBeNull("a removed secret stops at once, on a connection already open too");
    }

    [Fact]
    public async Task A_projects_value_is_given_only_while_a_command_of_that_project_runs()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SecretWorld(provider);
        var projectId = await world.Read(async db =>
        {
            var project = new Project { Name = "Launch", Instructions = string.Empty };
            db.Projects.Add(project);
            await Task.CompletedTask;
            return project.Id;
        });
        var value = NewValue();
        await AddSecret(world, Brokered("LAUNCH_KEY", value, "project") with { ProjectId = projectId });
        var host = world.StartHost();
        await host.Services.GetRequiredService<SandboxBroker>().Run(Call(world, world.BotId), null, "echo hi", null, null, RunWorld.Token);
        var spec = provider.Specs.Single();
        var placeholder = spec.Environment["LAUNCH_KEY"];
        var service = host.Services.GetRequiredService<CredentialBrokerService>();
        var session = (await service.Authenticate(TokenOf(spec), RunWorld.Token))!;

        var outside = await service.Substitute(session, "api.example.test", [(placeholder, BrokerPlacement.Header)], RunWorld.Token);
        BrokerSubstitution inside;

        using (host.Services.GetRequiredService<SandboxCommandProjects>().Hold(session.SandboxId, projectId))
        {
            inside = await service.Substitute(session, "api.example.test", [(placeholder, BrokerPlacement.Header)], RunWorld.Token);
        }

        outside.Refusal.Should().NotBeNull();
        inside.Values[placeholder].Should().Be(value);
    }

    #endregion

    #region Helpers

    private async Task<RunWorld> SecretWorld(FakeSandboxProvider provider, bool brokerSetUp = true, ScriptedModel? script = null)
    {
        var world = await RunWorld.Create(
            postgres,
            script ?? new ScriptedModel([new TextOutput("Done.")]),
            everyHost: services =>
            {
                services.AddSingleton<ISandboxProvider>(provider);
                services.AddSandboxTools();

                if (brokerSetUp)
                {
                    services.Configure<CredentialBrokerOptions>(options =>
                    {
                        options.PublicHost = BrokerHost;
                        options.CertificatePath = "listener.pem";
                    });
                }
            });
        await world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == world.BotId, RunWorld.Token);
            bot.SandboxPolicy = SandboxPolicy.Private;
            bot.SandboxSettings = new SandboxSettings { Network = NetworkAccess.Off };
            return true;
        });
        return world;
    }

    private static Task<Guid> AddBot(RunWorld world, string handle) =>
        world.Read(async db =>
        {
            var bot = new Bot { Name = handle, Handle = handle, ModelConnectionId = world.ConnectionId, Model = "scripted", SandboxPolicy = SandboxPolicy.Private };
            db.Bots.Add(bot);
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = bot.Id, DescendantBotId = bot.Id, Distance = 0 });
            await Task.CompletedTask;
            return bot.Id;
        });

    private static async Task<SandboxSecretResponse> AddSecret(RunWorld world, CreateSandboxSecretRequest request)
    {
        var change = await world.Transact(services => services.GetRequiredService<SandboxSecretService>().Create(request, world.UserId, RunWorld.Token));
        change.Problem.Should().BeNull();
        return change.Secret!;
    }

    private static CreateSandboxSecretRequest Brokered(string name, string value, string scope, IReadOnlyList<Guid>? botIds = null) =>
        new(name, null, value, "brokered", ["api.example.test"], false, false, scope, botIds, null);

    private static CreateSandboxSecretRequest Raw(string name, string value) =>
        new(name, null, value, "raw", [], false, false, "space", null, null, ConfirmRawMode: true);

    private static string NewValue() => $"test-{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";

    /// <summary>The token in the proxy address the sandbox was given.</summary>
    private static string TokenOf(SandboxSpec spec)
    {
        var proxy = new Uri(spec.Environment["HTTPS_PROXY"]);
        return proxy.UserInfo.Split(':')[1];
    }

    private static BuiltinToolCall Call(RunWorld world, Guid botId) =>
        new(
            JsonElement.Parse("{}"),
            botId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            world.Hosts[^1].Services.GetRequiredService<RunScopes>().For(world.SpaceId, world.UserId),
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { });

    #endregion
}
