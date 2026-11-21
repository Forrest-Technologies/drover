namespace Drover.Engine.Tests.Permissions;

/// <summary>
/// Allow-all windows against a real database and a clock that stands still until a test moves it. Each test opens
/// its own tenant scope, as the service's callers do, and commits it before reading the result back.
/// </summary>
public sealed class AllowAllServiceTests(PostgresServer postgres) : IAsyncLifetime
{
    #region Private Fields

    private readonly DateTimeOffset started = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private MovableClock clock = null!;

    private EngineHost host = null!;

    private RunWorld world = null!;

    #endregion

    #region Public Methods

    public async ValueTask InitializeAsync()
    {
        clock = new MovableClock(started);
        world = await RunWorld.Create(
            postgres,
            new ScriptedModel(),
            everyHost: services => services.AddSingleton<TimeProvider>(clock));
        host = world.IdleHost();
    }

    public async ValueTask DisposeAsync()
    {
        await host.DisposeAsync();
        await world.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Opening_a_window_uses_the_server_clock_and_records_the_start()
    {
        var callId = Guid.CreateVersion7();
        var window = await Open(world.BotId, world.UserId, AllowAllWindowKind.Minutes30, AllowAllWindowSource.Approval, callId, world.UserId);

        window.StartedAt.Should().Be(started);
        window.EndsAt.Should().Be(started.AddMinutes(30));
        window.StartedFrom.Should().Be(AllowAllWindowSource.Approval);
        window.ToolCallId.Should().Be(callId);
        window.EndedAt.Should().BeNull();

        var entries = await Audit(world.UserId);
        var start = entries.Single(entry => entry.Action == AllowAllService.StartedAction);
        start.ActorId.Should().Be(world.UserId);
        start.TargetKind.Should().Be(AuditTargets.AllowAllWindow);
        start.TargetId.Should().Be(window.Id);
        start.Data.GetProperty("kind").GetString().Should().Be("minutes_30");
        start.Data.GetProperty("started_from").GetString().Should().Be("approval");
        start.Data.GetProperty("tool_call_id").GetGuid().Should().Be(callId);
    }

    [Fact]
    public async Task Opening_again_ends_the_open_window_for_that_bot_and_person_and_leaves_one_open()
    {
        var first = await Open(world.BotId, world.UserId, AllowAllWindowKind.Minutes30, AllowAllWindowSource.Bot, null, world.UserId);
        clock.Advance(TimeSpan.FromMinutes(2));
        var second = await Open(world.BotId, world.UserId, AllowAllWindowKind.Hour1, AllowAllWindowSource.Bot, null, world.UserId);

        var stored = await Read(world.UserId, first.Id);
        stored.EndedAt.Should().Be(started.AddMinutes(2));
        stored.EndedReason.Should().Be(AllowAllWindowEndReason.AskedAgain);
        (await Read(world.UserId, second.Id)).EndedAt.Should().BeNull();

        var open = await InScope(world.UserId, services => services.GetRequiredService<AllowAllWindowRepository>()
            .ListOpenForBot(world.BotId, RunWorld.Token));
        open.Select(window => window.Id).Should().Equal(second.Id);

        var entries = await Audit(world.UserId);
        entries.Count(entry => entry.Action == AllowAllService.StartedAction).Should().Be(2);
        entries.Count(entry => entry.Action == AllowAllService.EndedAction).Should().Be(1);
        entries.Single(entry => entry.Action == AllowAllService.EndedAction)
            .Data.GetProperty("ended_reason").GetString().Should().Be("asked_again");
    }

    [Fact]
    public async Task Opening_over_an_expired_window_records_expired_not_asked_again()
    {
        var expired = await Open(world.BotId, world.UserId, AllowAllWindowKind.Minutes30, AllowAllWindowSource.Bot, null, world.UserId);
        clock.Advance(TimeSpan.FromMinutes(31));

        var replacement = await Open(world.BotId, world.UserId, AllowAllWindowKind.Hour1, AllowAllWindowSource.Bot, null, world.UserId);

        (await Read(world.UserId, expired.Id)).EndedReason.Should().Be(AllowAllWindowEndReason.Expired);
        (await Read(world.UserId, replacement.Id)).EndedAt.Should().BeNull();
        (await Audit(world.UserId)).Should().ContainSingle(entry => entry.Action == AllowAllService.EndedAction
            && entry.Data.GetProperty("ended_reason").GetString() == "expired");
    }

    [Fact]
    public async Task Two_openings_for_one_bot_and_person_overlap_and_leave_one_window()
    {
        var first = InScope(world.UserId, services => services.GetRequiredService<AllowAllService>()
            .Open(world.BotId, world.UserId, AllowAllWindowKind.UntilOff, AllowAllWindowSource.Bot, null, world.UserId, RunWorld.Token));
        var second = InScope(world.UserId, services => services.GetRequiredService<AllowAllService>()
            .Open(world.BotId, world.UserId, AllowAllWindowKind.UntilOff, AllowAllWindowSource.Bot, null, world.UserId, RunWorld.Token));

        var both = await Task.WhenAll(first, second);

        both[0].Id.Should().NotBe(both[1].Id);
        var open = await InScope(world.UserId, services => services.GetRequiredService<AllowAllWindowRepository>()
            .ListOpenForBot(world.BotId, RunWorld.Token));
        open.Select(window => window.Id).Should().HaveCount(1);
        var ended = both.Single(window => window.Id != open[0].Id);
        (await Read(world.UserId, ended.Id)).EndedReason.Should().Be(AllowAllWindowEndReason.AskedAgain);
        (await Audit(world.UserId)).Should().ContainSingle(entry => entry.Action == AllowAllService.EndedAction
            && entry.Data.GetProperty("ended_reason").GetString() == "asked_again");
    }

    [Fact]
    public async Task Two_endings_of_one_window_overlap_and_only_one_writes_an_audit_row()
    {
        var window = await Open(world.BotId, world.UserId, AllowAllWindowKind.UntilOff, AllowAllWindowSource.Bot, null, world.UserId);
        var first = End(window.Id, AllowAllWindowEndReason.BotPaused, world.UserId);
        var second = End(window.Id, AllowAllWindowEndReason.BotRemoved, world.UserId);

        var decisions = await Task.WhenAll(first, second);

        decisions.Should().Equal(true, false);
        var stored = await Read(world.UserId, window.Id);
        stored.EndedReason.Should().Be(AllowAllWindowEndReason.BotPaused);
        (await Audit(world.UserId)).Should().ContainSingle(entry => entry.Action == AllowAllService.EndedAction);
    }

    [Fact]
    public async Task The_database_refuses_a_second_open_window_for_the_same_bot_and_person()
    {
        var first = NewWindow(world.BotId, world.UserId, AllowAllWindowKind.UntilOff);
        var second = NewWindow(world.BotId, world.UserId, AllowAllWindowKind.UntilOff);

        var insert = async () => await world.Transact(async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            db.AllowAllWindows.AddRange(first, second);
            await db.SaveChangesAsync(RunWorld.Token);
            return true;
        });
        var failure = await insert.Should().ThrowAsync<DbUpdateException>();

        failure.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task The_database_refuses_a_timed_window_without_an_end()
    {
        var window = NewWindow(world.BotId, world.UserId, AllowAllWindowKind.Minutes30);

        await AssertRefused(window);
    }

    [Fact]
    public async Task The_database_refuses_an_end_on_a_window_that_does_not_expire()
    {
        var window = NewWindow(world.BotId, world.UserId, AllowAllWindowKind.UntilDone);
        window.EndsAt = started.AddHours(1);

        await AssertRefused(window);
    }

    [Fact]
    public async Task The_database_refuses_an_approval_window_without_a_call()
    {
        var window = NewWindow(world.BotId, world.UserId, AllowAllWindowKind.UntilOff);
        window.StartedFrom = AllowAllWindowSource.Approval;

        await AssertRefused(window);
    }

    [Fact]
    public async Task Finding_a_window_uses_the_server_clock_and_ends_it_when_it_expires()
    {
        var window = await Open(world.BotId, world.UserId, AllowAllWindowKind.Minutes30, AllowAllWindowSource.Bot, null, world.UserId);

        clock.Advance(TimeSpan.FromMinutes(29).Add(TimeSpan.FromSeconds(59)));
        (await Find(world.BotId, world.UserId, world.UserId)).Should().NotBeNull();
        (await Read(world.UserId, window.Id)).EndedAt.Should().BeNull();

        clock.Advance(TimeSpan.FromSeconds(1));
        (await Find(world.BotId, world.UserId, world.UserId)).Should().BeNull();

        var ended = await Read(world.UserId, window.Id);
        ended.EndedAt.Should().Be(started.AddMinutes(30));
        ended.EndedReason.Should().Be(AllowAllWindowEndReason.Expired);
        (await Audit(world.UserId)).Single(entry => entry.Action == AllowAllService.EndedAction)
            .Data.GetProperty("ended_reason").GetString().Should().Be("expired");
    }

    [Fact]
    public async Task Finding_a_window_only_matches_its_bot_and_person()
    {
        var memberId = await world.AddMember(MembershipRole.Member);
        var childId = await world.NewBotBeneath(world.BotId, "Child");
        await Open(world.BotId, world.UserId, AllowAllWindowKind.UntilOff, AllowAllWindowSource.Bot, null, world.UserId);

        (await Find(world.BotId, memberId, memberId)).Should().BeNull();
        (await Find(childId, world.UserId, world.UserId)).Should().BeNull();
        (await Find(world.BotId, world.UserId, world.UserId)).Should().NotBeNull();
    }

    [Theory]
    [InlineData(AllowAllWindowEndReason.Expired)]
    [InlineData(AllowAllWindowEndReason.WorkDone)]
    [InlineData(AllowAllWindowEndReason.AskedAgain)]
    [InlineData(AllowAllWindowEndReason.BotPaused)]
    [InlineData(AllowAllWindowEndReason.BotRemoved)]
    [InlineData(AllowAllWindowEndReason.MemberLeft)]
    public async Task Ending_a_window_records_the_reason_once(AllowAllWindowEndReason reason)
    {
        var window = await Open(world.BotId, world.UserId, AllowAllWindowKind.UntilOff, AllowAllWindowSource.Bot, null, world.UserId);
        clock.Advance(TimeSpan.FromMinutes(3));

        (await End(window.Id, reason, world.UserId)).Should().BeTrue();
        (await End(window.Id, reason, world.UserId)).Should().BeFalse();

        var stored = await Read(world.UserId, window.Id);
        stored.EndedAt.Should().Be(started.AddMinutes(3));
        stored.EndedReason.Should().Be(reason);
        (await Audit(world.UserId)).Count(entry => entry.Action == AllowAllService.EndedAction).Should().Be(1);
    }

    [Fact]
    public async Task Ending_a_bots_windows_ends_the_windows_the_caller_can_see_with_the_bot_reason()
    {
        var memberId = await world.AddMember(MembershipRole.Member);
        await Open(world.BotId, world.UserId, AllowAllWindowKind.UntilOff, AllowAllWindowSource.Bot, null, world.UserId);
        await Open(world.BotId, memberId, AllowAllWindowKind.UntilOff, AllowAllWindowSource.Bot, null, memberId);

        await EndForBot(world.UserId);
        (await Find(world.BotId, world.UserId, world.UserId)).Should().BeNull();

        // Row-level security hides the member's window from the owner. The member's own transaction ends it.
        (await Find(world.BotId, memberId, memberId)).Should().NotBeNull();
        await EndForBot(memberId);
        (await Find(world.BotId, memberId, memberId)).Should().BeNull();

        // The audit log has no audience: both entries are visible to either caller, with their own actor recorded.
        foreach (var userId in new[] { world.UserId, memberId })
        {
            (await Audit(userId)).Where(entry => entry.Action == AllowAllService.EndedAction && entry.ActorId == userId)
                .Should().ContainSingle()
                .Which.Data.GetProperty("ended_reason").GetString().Should().Be("bot_paused");
        }
    }

    [Fact]
    public async Task Ending_a_persons_windows_leaves_other_peoples_windows_open()
    {
        var memberId = await world.AddMember(MembershipRole.Member);
        await Open(world.BotId, world.UserId, AllowAllWindowKind.UntilOff, AllowAllWindowSource.Bot, null, world.UserId);
        await Open(world.BotId, memberId, AllowAllWindowKind.UntilOff, AllowAllWindowSource.Bot, null, memberId);

        await InScope<bool>(memberId, async services =>
        {
            await services.GetRequiredService<AllowAllService>()
                .EndForAudience(memberId, AllowAllWindowEndReason.MemberLeft, memberId, RunWorld.Token);
            return true;
        });

        (await Find(world.BotId, memberId, memberId)).Should().BeNull();
        (await Find(world.BotId, world.UserId, world.UserId)).Should().NotBeNull();
        (await Audit(memberId)).Should().ContainSingle(entry => entry.Action == AllowAllService.EndedAction
            && entry.Data.GetProperty("ended_reason").GetString() == "member_left");
        (await Audit(world.UserId)).Should().NotContain(entry => entry.Action == AllowAllService.EndedAction
            && entry.ActorId == world.UserId);
    }

    [Theory]
    [InlineData(RunStatus.Queued)]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Waiting)]
    public async Task An_until_done_window_stays_open_while_the_bot_has_work_for_the_person(RunStatus status)
    {
        var window = await Open(world.BotId, world.UserId, AllowAllWindowKind.UntilDone, AllowAllWindowSource.Bot, null, world.UserId);
        var runId = await AddRun(status);

        await EndIfIdle();
        (await Read(world.UserId, window.Id)).EndedAt.Should().BeNull();

        await SetRunStatus(runId, RunStatus.Completed);
        await EndIfIdle();
        (await Read(world.UserId, window.Id)).EndedReason.Should().Be(AllowAllWindowEndReason.WorkDone);
    }

    [Fact]
    public async Task An_until_done_window_ends_when_only_another_person_has_work()
    {
        var memberId = await world.AddMember(MembershipRole.Member);
        var window = await Open(world.BotId, world.UserId, AllowAllWindowKind.UntilDone, AllowAllWindowSource.Bot, null, world.UserId);
        await AddRun(RunStatus.Running, memberId);

        await EndIfIdle();

        (await Read(world.UserId, window.Id)).EndedReason.Should().Be(AllowAllWindowEndReason.WorkDone);
    }

    #endregion

    #region Private Methods

    private async Task AssertRefused(AllowAllWindow window)
    {
        var insert = async () => await world.Transact(async services =>
        {
            services.GetRequiredService<DroverDbContext>().AllowAllWindows.Add(window);
            await services.GetRequiredService<DroverDbContext>().SaveChangesAsync(RunWorld.Token);
            return true;
        });
        var failure = await insert.Should().ThrowAsync<DbUpdateException>();

        failure.Which.InnerException.Should().BeOfType<PostgresException>()
            .Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    private AllowAllWindow NewWindow(Guid botId, Guid audienceUserId, AllowAllWindowKind kind) => new()
    {
        BotId = botId,
        AudienceUserId = audienceUserId,
        Kind = kind,
        StartedAt = clock.GetUtcNow(),
        StartedFrom = AllowAllWindowSource.Bot,
    };

    private Task<AllowAllWindow> Open(
        Guid botId,
        Guid audienceUserId,
        AllowAllWindowKind kind,
        AllowAllWindowSource source,
        Guid? toolCallId,
        Guid actorUserId) =>
        InScope(actorUserId, services => services.GetRequiredService<AllowAllService>()
            .Open(botId, audienceUserId, kind, source, toolCallId, actorUserId, RunWorld.Token));

    private Task<AllowAllWindow?> Find(Guid botId, Guid audienceUserId, Guid asUserId) =>
        InScope(asUserId, services => services.GetRequiredService<AllowAllService>()
            .FindOpen(botId, audienceUserId, RunWorld.Token));

    private Task<bool> End(Guid windowId, AllowAllWindowEndReason reason, Guid actorUserId) =>
        InScope(actorUserId, services => services.GetRequiredService<AllowAllService>()
            .End(windowId, reason, actorUserId, RunWorld.Token));

    private Task<bool> EndForBot(Guid asUserId) =>
        InScope<bool>(asUserId, async services =>
        {
            await services.GetRequiredService<AllowAllService>()
                .EndForBot(world.BotId, AllowAllWindowEndReason.BotPaused, asUserId, RunWorld.Token);
            return true;
        });

    private Task<bool> EndIfIdle() =>
        InScope<bool>(world.UserId, async services =>
        {
            await services.GetRequiredService<AllowAllService>()
                .EndUntilDoneIfIdle(world.BotId, world.UserId, RunWorld.Token);
            return true;
        });

    private Task<AllowAllWindow> Read(Guid asUserId, Guid windowId) =>
        InScope(asUserId, services => services.GetRequiredService<DroverDbContext>()
            .AllowAllWindows.AsNoTracking().SingleAsync(window => window.Id == windowId, RunWorld.Token));

    private Task<List<AuditLogEntry>> Audit(Guid asUserId) =>
        InScope(asUserId, services => services.GetRequiredService<DroverDbContext>()
            .AuditLog.AsNoTracking()
            .Where(entry => entry.TargetKind == AuditTargets.AllowAllWindow)
            .OrderBy(entry => entry.Id)
            .ToListAsync(RunWorld.Token));

    private Task<Guid> AddRun(RunStatus status, Guid? principalUserId = null)
    {
        var run = new Run
        {
            ThreadId = Guid.CreateVersion7(),
            BotId = world.BotId,
            PrincipalUserId = principalUserId ?? world.UserId,
            RootRunId = Guid.Empty,
            Status = status,
        };
        run.RootRunId = run.Id;
        return world.Transact(async services =>
        {
            services.GetRequiredService<DroverDbContext>().Runs.Add(run);
            await services.GetRequiredService<DroverDbContext>().SaveChangesAsync(RunWorld.Token);
            return run.Id;
        });
    }

    private Task<bool> SetRunStatus(Guid runId, RunStatus status) =>
        world.Transact(async services =>
        {
            var db = services.GetRequiredService<DroverDbContext>();
            var run = await db.Runs.SingleAsync(row => row.Id == runId, RunWorld.Token);
            run.Status = status;
            await db.SaveChangesAsync(RunWorld.Token);
            return true;
        });

    private async Task<T> InScope<T>(Guid userId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(world.SpaceId, userId);
        await using var transaction = await scope.ServiceProvider.GetRequiredService<TenantScope>().Begin(RunWorld.Token);
        var result = await work(scope.ServiceProvider);
        await transaction.Commit(RunWorld.Token);
        return result;
    }

    #endregion

    #region Test Clock

    /// <summary>A clock that stands still until a test moves it.</summary>
    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        #region Private Fields

        private DateTimeOffset current = start;

        #endregion

        #region Public Methods

        public void Advance(TimeSpan by) => current = current.Add(by);

        public override DateTimeOffset GetUtcNow() => current;

        #endregion
    }

    #endregion
}
