namespace Drover.Engine.Tests.Sandboxes;

/// <summary>
/// What happens to a sandbox nobody will come back for, over PostgreSQL and the real engine, against a provider in
/// memory: a removed bot's sandbox is retired, paused, and destroyed when its retention is over; a member that leaves
/// a shared sandbox only loses it; a creation left behind stops blocking its bot; and one sandbox the provider cannot
/// destroy waits its turn without holding up the rest. The sweep runs as if at a later moment where a test needs it.
/// </summary>
public sealed class SandboxSweepTests(PostgresServer postgres)
{
    #region Removing A Bot

    [Fact]
    public async Task A_removed_bots_sandbox_is_retired_paused_at_once_and_destroyed_with_its_token_when_its_retention_is_over()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.WriteFile(Call(world, world.BotId), null, "/home/user/notes.txt", "keep me", RunWorld.Token);
        var made = await ReadSandbox(world, world.BotId);

        await Remove(world, world.BotId);
        var retired = await ReadSandbox(world, world.BotId);
        var now = await Sweep(world, provider, TimeSpan.Zero);
        var paused = await ReadSandbox(world, world.BotId);
        var nothingMore = await Sweep(world, provider, TimeSpan.FromDays(6));
        var whenDue = await Sweep(world, provider, SandboxSweep.Retention + TimeSpan.FromMinutes(1));
        var destroyed = await ReadSandbox(world, world.BotId);

        retired.State.Should().Be(SandboxState.Retiring);
        retired.RetireAfter.Should().BeCloseTo(DateTimeOffset.UtcNow + SandboxSweep.Retention, TimeSpan.FromMinutes(1), "kept for the retention period");
        now.Should().Be(1);
        paused.State.Should().Be(SandboxState.Retiring, "a person can still recover its files");
        paused.AccessSecretId.Should().Be(made.AccessSecretId);
        provider.Pauses.Should().ContainSingle();
        nothingMore.Should().Be(0, "a paused sandbox is not paused again, and it is not due yet");
        whenDue.Should().Be(1);
        provider.Only().State.Should().Be(ProviderSandboxState.Gone);
        destroyed.State.Should().Be(SandboxState.Destroyed);
        destroyed.AccessSecretId.Should().BeNull();
        (await world.Read(db => db.Secrets.AnyAsync(secret => secret.Id == made.AccessSecretId, RunWorld.Token))).Should().BeFalse("its token is deleted");
        provider.Pauses.Concat(provider.Destroys).Should().OnlyContain(
            handle => handle.AccessToken.Length == 0 && handle.SharingGroupId == world.BotId && handle.SpaceId == world.SpaceId,
            "the sweep never reveals a token: pausing and destroying need none");
    }

    [Fact]
    public async Task A_member_that_is_removed_loses_a_shared_sandbox_which_carries_on_for_the_rest()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Named);
        var member = await AddBot(world, "member", null, SandboxPolicy.Private);
        await world.Read(db =>
        {
            db.BotSandboxShares.Add(new BotSandboxShare { BotId = world.BotId, SharedWithBotId = member });
            return Task.FromResult(true);
        });
        var ownerHandle = await world.Read(db => db.Bots.Where(bot => bot.Id == world.BotId).Select(bot => bot.Handle).SingleAsync(RunWorld.Token));
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.WriteFile(Call(world, member), ownerHandle, "/w/shared.txt", "ours", RunWorld.Token);
        await broker.WriteFile(Call(world, member), null, "/w/own.txt", "mine", RunWorld.Token);
        var shared = await ReadSandbox(world, world.BotId);

        await Remove(world, member);
        await Sweep(world, provider, SandboxSweep.Retention + TimeSpan.FromMinutes(1));
        var fromTheOwner = await broker.ReadFile(Call(world, world.BotId), null, "/w/shared.txt", RunWorld.Token);
        var fromTheMember = () => broker.ReadFile(Call(world, member), ownerHandle, "/w/shared.txt", RunWorld.Token);

        fromTheOwner.Content.Should().Be("ours");
        (await ReadSandbox(world, world.BotId)).Should().Match<Sandbox>(sandbox => sandbox.Id == shared.Id && sandbox.State == SandboxState.Running);
        await fromTheMember.Should().ThrowAsync<BuiltinToolException>("a removed bot can do nothing");
        (await world.Read(db => db.BotSandboxShares.AnyAsync(share => share.SharedWithBotId == member, RunWorld.Token))).Should().BeFalse();
        (await ReadSandbox(world, member)).State.Should().Be(SandboxState.Destroyed, "the member's own sandbox goes the way of any removed bot's");
        provider.Destroys.Should().ContainSingle().Which.SharingGroupId.Should().Be(member);
    }

    #endregion

    #region Creations Left Behind

    [Fact]
    public async Task A_row_left_creating_is_cleared_and_its_bot_can_get_a_sandbox_again()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        var leftBehind = Guid.CreateVersion7(DateTimeOffset.UtcNow - TimeSpan.FromMinutes(20));
        var fresh = await AddBot(world, "fresh", null, SandboxPolicy.Private);
        var justStarted = Guid.CreateVersion7();
        await world.Read(db =>
        {
            db.Sandboxes.Add(NewCreatingRow(leftBehind, world.BotId));
            db.Sandboxes.Add(NewCreatingRow(justStarted, fresh));
            return Task.FromResult(true);
        });
        var blocked = () => world.Read(db =>
        {
            db.Sandboxes.Add(NewCreatingRow(Guid.CreateVersion7(), world.BotId));
            return Task.FromResult(true);
        });
        await blocked.Should().ThrowAsync<DbUpdateException>("one live row per bot");

        var first = await Sweep(world, provider, TimeSpan.Zero);
        var taken = await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(sandbox => sandbox.Id == leftBehind, RunWorld.Token));
        var result = await broker.Run(Call(world, world.BotId), null, "echo again", null, null, RunWorld.Token);
        await Sweep(world, provider, TimeSpan.Zero);

        first.Should().Be(1, "the creation started a moment ago is left alone");
        taken.State.Should().Be(SandboxState.Retiring);
        result.ExitCode.Should().Be(0);
        var rows = await world.Read(db => db.Sandboxes.AsNoTracking().Where(sandbox => sandbox.OwnerBotId == world.BotId).ToListAsync(RunWorld.Token));
        rows.Should().HaveCount(2);
        rows.Single(sandbox => sandbox.Id == leftBehind).State.Should().Be(SandboxState.Destroyed);
        rows.Single(sandbox => sandbox.Id != leftBehind).State.Should().Be(SandboxState.Running);
        (await world.Read(db => db.Sandboxes.AsNoTracking().SingleAsync(sandbox => sandbox.Id == justStarted, RunWorld.Token))).State
            .Should().Be(SandboxState.Creating);
        provider.Created.Should().Be(1);
    }

    #endregion

    #region Failures

    [Fact]
    public async Task A_sandbox_the_provider_cannot_destroy_waits_for_a_later_pass_and_does_not_hold_up_the_others()
    {
        var provider = new FakeSandboxProvider();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var other = await AddBot(world, "other", null, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.Run(Call(world, world.BotId), null, "true", null, null, RunWorld.Token);
        await broker.Run(Call(world, other), null, "true", null, null, RunWorld.Token);
        var failing = await ReadSandbox(world, world.BotId);
        provider.FailingDestroys[failing.ProviderSandboxId] = true;
        await Remove(world, world.BotId);
        await Remove(world, other);
        var due = SandboxSweep.Retention + TimeSpan.FromMinutes(1);

        var first = await Sweep(world, provider, due);
        var attempts = provider.Destroys.Count;
        var again = await Sweep(world, provider, due + TimeSpan.FromMinutes(5));
        var attemptsAfterAgain = provider.Destroys.Count;
        provider.FailingDestroys.Clear();
        var later = await Sweep(world, provider, due + SandboxSweep.RetryAfter + TimeSpan.FromMinutes(1));

        first.Should().Be(1, "the other bot's sandbox is destroyed in the same pass");
        (await ReadSandbox(world, other)).State.Should().Be(SandboxState.Destroyed);
        attempts.Should().Be(2);
        again.Should().Be(0);
        attemptsAfterAgain.Should().Be(attempts, "a sandbox that failed is not tried again at once");
        later.Should().Be(1);
        (await ReadSandbox(world, world.BotId)).State.Should().Be(SandboxState.Destroyed);
    }

    [Fact]
    public async Task A_use_that_ends_after_its_bot_was_removed_leaves_the_sandbox_retiring()
    {
        var provider = new FakeSandboxProvider();
        var started = new TaskCompletionSource();
        var removed = new TaskCompletionSource();
        await using var world = await SandboxWorld(provider);
        await GiveSandbox(world, world.BotId, SandboxPolicy.Private);
        var broker = world.StartHost().Services.GetRequiredService<SandboxBroker>();
        await broker.Run(Call(world, world.BotId), null, "true", null, null, RunWorld.Token);
        provider.Behaviour = (_, _, cancellationToken) => RunUntil(started, removed.Task, cancellationToken);

        var running = broker.Run(Call(world, world.BotId), null, "sleep 1", null, null, RunWorld.Token);
        await started.Task.WaitAsync(RunWorld.Token);
        await Remove(world, world.BotId);
        removed.SetResult();
        await running;

        (await ReadSandbox(world, world.BotId)).State.Should().Be(SandboxState.Retiring, "the call that finished does not bring it back into use");
    }

    #endregion

    #region Helpers

    private async Task<RunWorld> SandboxWorld(FakeSandboxProvider provider) =>
        await RunWorld.Create(
            postgres,
            new ScriptedModel([new TextOutput("Done.")]),
            everyHost: services =>
            {
                services.AddSingleton<ISandboxProvider>(provider);
                services.AddSandboxTools();
            });

    /// <summary>Runs one pass of the sweep as if <paramref name="later"/> had gone by.</summary>
    private static Task<int> Sweep(RunWorld world, FakeSandboxProvider provider, TimeSpan later) =>
        new SandboxSweep(
                world.Hosts[^1].Services.GetRequiredService<RunScopes>(),
                provider,
                new LaterTime(later),
                world.Hosts[^1].Services.GetRequiredService<ILogger<SandboxSweep>>())
            .Sweep(RunWorld.Token);

    /// <summary>Removes the bot through the engine's own service, as the bot editor does.</summary>
    private static Task<bool> Remove(RunWorld world, Guid botId) =>
        world.Transact(async services =>
        {
            (await services.GetRequiredService<BotService>().Remove(botId, world.UserId, RunWorld.Token)).Bot.Should().NotBeNull();
            return true;
        });

    /// <summary>The bot's newest sandbox row.</summary>
    private static Task<Sandbox> ReadSandbox(RunWorld world, Guid botId) =>
        world.Read(db => db.Sandboxes.AsNoTracking().Where(sandbox => sandbox.OwnerBotId == botId).OrderByDescending(sandbox => sandbox.Id).FirstAsync(RunWorld.Token));

    private static Sandbox NewCreatingRow(Guid id, Guid botId) => new()
    {
        Id = id,
        OwnerBotId = botId,
        Provider = "fake",
        ProviderSandboxId = string.Empty,
        State = SandboxState.Creating,
    };

    private static Task<bool> GiveSandbox(RunWorld world, Guid botId, SandboxPolicy policy) =>
        world.Read(async db =>
        {
            var bot = await db.Bots.SingleAsync(found => found.Id == botId, RunWorld.Token);
            bot.SandboxPolicy = policy;
            bot.SandboxSettings = new SandboxSettings { Network = NetworkAccess.Off };
            return true;
        });

    /// <summary>A bot at the top, with its closure row, as the tree keeps it.</summary>
    private static Task<Guid> AddBot(RunWorld world, string handle, Guid? parentId, SandboxPolicy policy) =>
        world.Read(db =>
        {
            var bot = new Bot
            {
                Name = handle,
                Handle = handle,
                ModelConnectionId = world.ConnectionId,
                Model = "scripted",
                ParentBotId = parentId,
                SandboxPolicy = policy,
            };
            db.Bots.Add(bot);
            db.BotAncestors.Add(new BotAncestor { AncestorBotId = bot.Id, DescendantBotId = bot.Id, Distance = 0 });
            return Task.FromResult(bot.Id);
        });

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

    /// <summary>A command that says it started and runs until the test says so.</summary>
    private static async IAsyncEnumerable<CommandOutput> RunUntil(TaskCompletionSource started, Task done, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new CommandStarted(1);
        started.TrySetResult();
        await done.WaitAsync(cancellationToken);
        yield return new CommandExited(0, null);
    }

    #endregion

    #region Nested Types

    /// <summary>The clock as it will read once <paramref name="later"/> has gone by.</summary>
    private sealed class LaterTime(TimeSpan later) : TimeProvider
    {
        #region Public Methods

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + later;

        #endregion
    }

    #endregion
}
