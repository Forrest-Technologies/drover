namespace Drover.Data.Tests;

/// <summary>
/// The lock that keeps a command and a tainted write from using one sandbox at once, and the bounds that keep it from
/// costing the server its database sessions: waiters queue in the process and hold no session, and the sessions that
/// are held are few, whatever a caller asks for.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class SandboxCommandLocksTests(PostgresFixture postgres)
{
    #region Private Fields

    private static readonly TimeSpan shortWait = TimeSpan.FromMilliseconds(300);

    #endregion

    #region Public Methods

    [Fact]
    public async Task Two_commands_for_one_sandbox_take_turns_and_the_waiter_holds_no_database_session()
    {
        var name = AppName();
        var locks = Locks(name);
        var space = Guid.NewGuid();
        var sandbox = Guid.NewGuid();
        var first = await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);

        var second = locks.Hold(space, sandbox, TestContext.Current.CancellationToken);

        await Task.Delay(shortWait, TestContext.Current.CancellationToken);
        second.IsCompleted.Should().BeFalse("the first command still holds the sandbox");
        (await Backends(name)).Should().Be(1, "a waiter queues in the process and opens no session");
        await first.DisposeAsync();
        await using var taken = await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        (await Backends(name)).Should().Be(1);
    }

    [Fact]
    public async Task Waiters_past_the_queue_bound_are_refused_before_any_session_is_opened()
    {
        var name = AppName();
        var locks = Locks(name, new SandboxCommandLimits { MaxQueuedPerSandbox = 4 });
        var space = Guid.NewGuid();
        var sandbox = Guid.NewGuid();
        var holder = await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);
        List<Task<SandboxCommandGate>> waiting = [.. Enumerable.Range(0, 3).Select(_ => locks.Hold(space, sandbox, TestContext.Current.CancellationToken))];

        var extra = async () => await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);

        await extra.Should().ThrowAsync<SandboxBusyException>().WithMessage("Too many commands are waiting*");
        (await Backends(name)).Should().Be(1, "one session for the holder and none for the three waiters or the refused caller");

        foreach (var queued in waiting)
        {
            await holder.DisposeAsync();
            holder = await queued.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        await holder.DisposeAsync();
    }

    [Fact]
    public async Task One_space_cannot_hold_more_sandboxes_than_its_bound_and_another_space_still_can()
    {
        var name = AppName();
        var locks = Locks(name, new SandboxCommandLimits { MaxHeldPerSpace = 2 });
        var space = Guid.NewGuid();
        await using var one = await locks.Hold(space, Guid.NewGuid(), TestContext.Current.CancellationToken);
        await using var two = await locks.Hold(space, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var third = async () => await locks.Hold(space, Guid.NewGuid(), TestContext.Current.CancellationToken);
        await third.Should().ThrowAsync<SandboxBusyException>();
        await using var other = await locks.Hold(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        (await Backends(name)).Should().Be(3);
    }

    [Fact]
    public async Task The_server_holds_no_more_sessions_than_its_bound_and_the_refused_ones_open_none()
    {
        var name = AppName();
        var locks = Locks(name, new SandboxCommandLimits { MaxHeld = 3, MaxHeldPerSpace = 3 });
        List<SandboxCommandGate> gates = [];
        for (var index = 0; index < 3; index++)
        {
            gates.Add(await locks.Hold(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken));
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var refused = async () => await locks.Hold(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);
            await refused.Should().ThrowAsync<SandboxBusyException>();
        }

        (await Backends(name)).Should().Be(3);

        foreach (var gate in gates)
        {
            await gate.DisposeAsync();
        }

        (await Backends(name)).Should().Be(0);
    }

    [Fact]
    public async Task A_waiter_that_waits_too_long_is_refused_with_a_sentence_and_leaves_the_queue()
    {
        var locks = Locks(AppName(), new SandboxCommandLimits { WaitForTurn = TimeSpan.FromMilliseconds(200), MaxQueuedPerSandbox = 2 });
        var space = Guid.NewGuid();
        var sandbox = Guid.NewGuid();
        var holder = await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);

        var waiter = async () => await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);

        await waiter.Should().ThrowAsync<SandboxBusyException>().WithMessage("This sandbox is still busy*");
        await holder.DisposeAsync();
        await using var later = await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_waiter_that_is_cancelled_leaves_the_queue()
    {
        var locks = Locks(AppName(), new SandboxCommandLimits { MaxQueuedPerSandbox = 2 });
        var space = Guid.NewGuid();
        var sandbox = Guid.NewGuid();
        await using var holder = await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);
        using var cancel = new CancellationTokenSource();
        var cancelled = locks.Hold(space, sandbox, cancel.Token);
        var full = async () => await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);
        await full.Should().ThrowAsync<SandboxBusyException>();

        await cancel.CancelAsync();
        await cancelled.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();

        var again = locks.Hold(space, sandbox, TestContext.Current.CancellationToken);
        again.IsFaulted.Should().BeFalse("the cancelled waiter gave its place back");
        await holder.DisposeAsync();
        await using var taken = await again.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_lock_whose_connection_dropped_is_released_without_an_error_and_gives_its_room_back()
    {
        var name = AppName();
        var locks = Locks(name, new SandboxCommandLimits { MaxHeld = 1, MaxHeldPerSpace = 1 });
        var space = Guid.NewGuid();
        var sandbox = Guid.NewGuid();
        var gate = await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);
        await TerminateBackends(name);

        var releasing = async () => await gate.DisposeAsync();

        await releasing.Should().NotThrowAsync();
        await using var next = await locks.Hold(space, sandbox, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Disposing_a_gate_twice_gives_back_its_room_once()
    {
        var locks = Locks(AppName(), new SandboxCommandLimits { MaxHeld = 1, MaxHeldPerSpace = 1 });
        var space = Guid.NewGuid();
        var gate = await locks.Hold(space, Guid.NewGuid(), TestContext.Current.CancellationToken);
        await gate.DisposeAsync();
        await gate.DisposeAsync();
        await using var again = await locks.Hold(space, Guid.NewGuid(), TestContext.Current.CancellationToken);

        var past = async () => await locks.Hold(space, Guid.NewGuid(), TestContext.Current.CancellationToken);

        await past.Should().ThrowAsync<SandboxBusyException>("the bound is one, however many times the first gate was disposed");
    }

    [Fact]
    public async Task Another_server_process_still_waits_for_the_holder_through_the_database_lock()
    {
        var space = Guid.NewGuid();
        var sandbox = Guid.NewGuid();
        var here = Locks(AppName());
        var elsewhere = Locks(AppName());
        var holder = await here.Hold(space, sandbox, TestContext.Current.CancellationToken);

        var waiting = elsewhere.Hold(space, sandbox, TestContext.Current.CancellationToken);

        await Task.Delay(shortWait, TestContext.Current.CancellationToken);
        waiting.IsCompleted.Should().BeFalse("the database lock keeps a second process out");
        await holder.DisposeAsync();
        await using var taken = await waiting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    #endregion

    #region Private Methods

    private static string AppName() => $"drover-locks-{Guid.NewGuid():N}";

    private SandboxCommandLocks Locks(string applicationName, SandboxCommandLimits? limits = null) =>
        new(
            postgres.AppConnectionString(applicationName: applicationName),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SandboxCommandLocks>.Instance,
            limits);

    private async Task<long> Backends(string applicationName)
    {
        await using var connection = new NpgsqlConnection(postgres.AdminConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE application_name = @name", connection);
        command.Parameters.AddWithValue("name", applicationName);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private async Task TerminateBackends(string applicationName)
    {
        await using var connection = new NpgsqlConnection(postgres.AdminConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = @name", connection);
        command.Parameters.AddWithValue("name", applicationName);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    #endregion
}
