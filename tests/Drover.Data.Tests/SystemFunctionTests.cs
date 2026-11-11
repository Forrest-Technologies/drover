namespace Drover.Data.Tests;

/// <summary>
/// The first functions in schema <c>sys</c>: what each returns, that each writes <c>sys.audit</c>, and that each keeps
/// the rules of docs/architecture/04-tenancy-and-permissions.md. <c>claim_runs</c> has its own tests in
/// <see cref="RunClaimTests"/>.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class SystemFunctionTests(PostgresFixture postgres)
{
    #region Private Fields

    private static readonly string[] functionNames =
        ["claim_runs", "find_user_for_sign_in", "memberships_for_user", "resolve_oauth_state", "purge_expired", "health"];

    #endregion

    #region Sign-In

    [Fact]
    public async Task Sign_in_finds_a_user_by_email_with_no_tenant_set_and_ignores_case()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var email = $"{world.UserA2:N}@Example.Test";

        var found = await Query(postgres.AppConnectionString(), "SELECT user_id, display_name FROM sys.find_user_for_sign_in(@email)", ("email", email));
        var unknown = await Query(postgres.AppConnectionString(), "SELECT user_id FROM sys.find_user_for_sign_in(@email)", ("email", "nobody@example.test"));

        found.Should().ContainSingle().Which[0].Should().Be(world.UserA2);
        unknown.Should().BeEmpty();
    }

    [Fact]
    public async Task Memberships_are_listed_for_a_user_before_any_space_is_chosen()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);

        var traveler = await Query(postgres.AppConnectionString(), "SELECT space_id, kind, role FROM sys.memberships_for_user(@user)", ("user", world.Traveler));
        var owner = await Query(postgres.AppConnectionString(), "SELECT space_id, role FROM sys.memberships_for_user(@user)", ("user", world.UserA1));
        var stranger = await Query(postgres.AppConnectionString(), "SELECT space_id FROM sys.memberships_for_user(@user)", ("user", Guid.CreateVersion7()));

        traveler.Select(row => row[0]).Should().BeEquivalentTo([world.SpaceA, world.SpaceB]);
        traveler.Should().OnlyContain(row => (string)row[1]! == "shared" && (string)row[2]! == "member");
        owner.Should().ContainSingle().Which.Should().Equal(world.SpaceA, "owner");
        stranger.Should().BeEmpty();
    }

    #endregion

    #region OAuth

    [Fact]
    public async Task An_oauth_callback_with_no_tenant_resolves_to_the_space_and_user_that_started_it()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var flow = await SaveFlows(provider, world);

        var live = await Query(postgres.AppConnectionString(), "SELECT space_id, user_id, tool_connection_id FROM sys.resolve_oauth_state(@hash)", ("hash", flow.Live));
        var expired = await Query(postgres.AppConnectionString(), "SELECT space_id FROM sys.resolve_oauth_state(@hash)", ("hash", flow.Expired));
        var unknown = await Query(postgres.AppConnectionString(), "SELECT space_id FROM sys.resolve_oauth_state(@hash)", ("hash", "no-such-state"));

        live.Should().ContainSingle().Which.Should().Equal(world.SpaceA, world.UserA1, flow.ConnectionId);
        expired.Should().BeEmpty("a flow past its expiry no longer resolves");
        unknown.Should().BeEmpty();
    }

    #endregion

    #region Cleanup And Health

    [Fact]
    public async Task Purge_deletes_expired_flows_and_destroyed_sandboxes_nobody_mentions_and_at_most_the_limit()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await SaveCleanupRows(provider, world);

        var first = await Scalar<int>(scratch.AppConnectionString, "SELECT sys.purge_expired(@limit)", ("limit", 1));
        var rest = await Scalar<int>(scratch.AppConnectionString, "SELECT sys.purge_expired(@limit)", ("limit", 100));
        var again = await Scalar<int>(scratch.AppConnectionString, "SELECT sys.purge_expired(@limit)", ("limit", 100));

        first.Should().Be(2, "one flow and one sandbox, the limit being per kind of row");
        rest.Should().Be(3);
        again.Should().Be(0);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await session.Db.OAuthFlows.CountAsync(TestContext.Current.CancellationToken)).Should().Be(1, "the live flow stays");
        (await session.Db.Sandboxes.Select(sandbox => sandbox.State).ToListAsync(TestContext.Current.CancellationToken))
            .Should().BeEquivalentTo([SandboxState.Destroyed, SandboxState.Running], "the mentioned sandbox and the running one stay");
    }

    [Fact]
    public async Task Health_counts_runs_and_users_with_no_tenant_and_no_tenant_data()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();

        var before = await Query(scratch.AppConnectionString, "SELECT queued_runs, running_runs, waiting_runs, expired_leases, oldest_lease_expires_at, user_count FROM sys.health()");
        before.Should().ContainSingle().Which.Should().Equal(0L, 0L, 0L, 0L, null, 0L);

        var world = await TenantWorld.Seed(provider);
        await SaveRunsInEveryState(provider, world);
        var after = await Query(scratch.AppConnectionString, "SELECT queued_runs, running_runs, waiting_runs, expired_leases, user_count FROM sys.health()");

        after.Should().ContainSingle().Which.Should().Equal(2L, 2L, 1L, 1L, 4L);
    }

    #endregion

    #region Audit

    [Fact]
    public async Task Every_function_writes_one_audit_row_naming_the_caller_and_the_rows_it_returned()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        await SaveRunsInEveryState(provider, world);
        var calls = new (string Name, string Sql, int Rows, (string, object?)[] Parameters)[]
        {
            ("claim_runs", "SELECT * FROM sys.claim_runs(@worker, @limit)", 2, [("worker", "w"), ("limit", 2)]),
            ("find_user_for_sign_in", "SELECT * FROM sys.find_user_for_sign_in(@email)", 1, [("email", $"{world.UserA1:N}@example.test")]),
            ("memberships_for_user", "SELECT * FROM sys.memberships_for_user(@user)", 2, [("user", world.Traveler)]),
            ("resolve_oauth_state", "SELECT * FROM sys.resolve_oauth_state(@hash)", 0, [("hash", "none")]),
            ("purge_expired", "SELECT sys.purge_expired(@limit)", 0, [("limit", 10)]),
            ("health", "SELECT * FROM sys.health()", 1, []),
        };

        foreach (var call in calls)
        {
            await Query(scratch.AppConnectionString, call.Sql, call.Parameters);
        }

        var audit = await Query(AdminFor(scratch), "SELECT function_name, caller, row_count FROM sys.audit ORDER BY id");
        audit.Select(row => (string)row[0]!).Should().Equal(calls.Select(call => call.Name));
        audit.Should().OnlyContain(row => (string)row[1]! == "drover_app");
        audit.Select(row => (int)row[2]!).Should().Equal(calls.Select(call => call.Rows));
    }

    [Fact]
    public async Task The_application_role_can_neither_read_nor_write_the_audit_table()
    {
        await using var connection = new NpgsqlConnection(postgres.AppConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var read = () => new NpgsqlCommand("SELECT count(*) FROM sys.audit", connection).ExecuteScalarAsync(TestContext.Current.CancellationToken);
        var thrown = await read.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        var write = () => new NpgsqlCommand("INSERT INTO sys.audit (function_name, caller, row_count) VALUES ('x', 'x', 0)", connection).ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        var refused = await write.Should().ThrowAsync<PostgresException>();
        refused.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    #endregion

    #region Rules For A Function

    [Fact]
    public async Task Each_function_is_owned_by_the_system_role_runs_as_its_owner_pins_its_search_path_and_is_not_public()
    {
        var rows = await Query(
            postgres.AdminConnectionString,
            """
            SELECT p.proname,
                   pg_get_userbyid(p.proowner),
                   p.prosecdef,
                   coalesce(p.proconfig, ARRAY[]::text[]) @> ARRAY['search_path=pg_catalog, public'],
                   NOT EXISTS (SELECT 1 FROM aclexplode(coalesce(p.proacl, acldefault('f', p.proowner))) a WHERE a.grantee = 0),
                   has_function_privilege('drover_app', p.oid, 'EXECUTE')
            FROM pg_proc p
            WHERE p.pronamespace = 'sys'::regnamespace AND p.proname = ANY(@names)
            ORDER BY p.proname
            """,
            ("names", functionNames));

        rows.Select(row => (string)row[0]!).Should().BeEquivalentTo(functionNames);
        rows.Should().OnlyContain(row =>
            (string)row[1]! == "drover_system" && (bool)row[2]! && (bool)row[3]! && (bool)row[4]! && (bool)row[5]!);
    }

    [Fact]
    public async Task The_system_role_holds_exactly_the_table_privileges_its_functions_need()
    {
        var granted = await Query(
            postgres.AdminConnectionString,
            """
            SELECT table_schema || '.' || table_name, privilege_type
            FROM information_schema.table_privileges
            WHERE grantee = 'drover_system'
            """);

        granted.Select(row => $"{row[0]} {row[1]}").Should().BeEquivalentTo(
        [
            "sys.audit INSERT",
            "public.runs SELECT",
            "public.runs UPDATE",
            "public.tool_calls SELECT",
            "public.users SELECT",
            "public.memberships SELECT",
            "public.spaces SELECT",
            "public.oauth_flows SELECT",
            "public.oauth_flows DELETE",
            "public.sandboxes SELECT",
            "public.sandboxes DELETE",
        ]);
    }

    #endregion

    #region Private Methods

    private string AdminFor(ScratchDatabase scratch) =>
        new NpgsqlConnectionStringBuilder(postgres.AdminConnectionString)
        {
            Database = new NpgsqlConnectionStringBuilder(scratch.AppConnectionString).Database,
        }.ConnectionString;

    private static async Task<List<object?[]>> Query(string connectionString, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object?[]>();

        while (await reader.ReadAsync())
        {
            var row = new object?[reader.FieldCount];

            for (var index = 0; index < row.Length; index++)
            {
                row[index] = await reader.IsDBNullAsync(index) ? null : reader.GetValue(index);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static async Task<T> Scalar<T>(string connectionString, string sql, params (string Name, object? Value)[] parameters) =>
        (T)(await Query(connectionString, sql, parameters))[0][0]!;

    private static async Task<FlowSet> SaveFlows(IServiceProvider provider, TenantWorld world)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = NewToolConnection();
        session.Db.ToolConnections.Add(connection);
        var live = new OAuthFlow
        {
            StateHash = $"live-{Guid.NewGuid():N}",
            AudienceUserId = world.UserA1,
            ToolConnectionId = connection.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        var expired = new OAuthFlow
        {
            StateHash = $"expired-{Guid.NewGuid():N}",
            AudienceUserId = world.UserA1,
            ToolConnectionId = connection.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        session.Db.OAuthFlows.AddRange(live, expired);
        await session.Commit();
        return new FlowSet(live.StateHash, expired.StateHash, connection.Id);
    }

    private static ToolConnection NewToolConnection() => new()
    {
        Name = $"server-{Guid.NewGuid():N}",
        Slug = $"server-{Guid.NewGuid():N}",
        Url = "https://tools.example.test/mcp",
    };

    /// <summary>Three expired flows and a live one; two destroyed sandboxes nobody mentions, one a tool call mentions, and a running one.</summary>
    private static async Task SaveCleanupRows(IServiceProvider provider, TenantWorld world)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = NewToolConnection();
        var model = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(model);
        var tool = CoreRows.NewTool();
        var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
        var run = CoreRows.NewRun(bot, world.UserA1);
        var sandboxes = new[]
        {
            NewSandbox(bot, SandboxState.Destroyed),
            NewSandbox(bot, SandboxState.Destroyed),
            NewSandbox(bot, SandboxState.Destroyed),
            NewSandbox(bot, SandboxState.Running),
        };
        session.Db.ToolConnections.Add(connection);
        session.Db.ModelConnections.Add(model);
        session.Db.Tools.Add(tool);
        CoreRows.AddBot(session.Db, bot, []);
        session.Db.Threads.Add(thread);
        session.Db.Runs.Add(run);
        session.Db.Sandboxes.AddRange(sandboxes);
        session.Db.OAuthFlows.AddRange(Enumerable.Range(0, 4).Select(index => new OAuthFlow
        {
            StateHash = $"state-{index}-{Guid.NewGuid():N}",
            AudienceUserId = world.UserA1,
            ToolConnectionId = connection.Id,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(index == 0 ? 10 : -index),
        }));
        await session.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        session.Db.ToolCalls.Add(new ToolCall
        {
            RunId = run.Id,
            ThreadId = thread.Id,
            AudienceUserId = thread.AudienceUserId,
            ToolId = tool.Id,
            ToolName = "example",
            Arguments = CoreRows.EmptyJson(),
            SandboxId = sandboxes[2].Id,
        });
        await session.Commit();
    }

    private static Sandbox NewSandbox(Bot bot, SandboxState state) => new()
    {
        OwnerBotId = bot.Id,
        Provider = "test",
        ProviderSandboxId = Guid.NewGuid().ToString("N"),
        State = state,
    };

    /// <summary>Two queued, two running (one with an expired lease), one waiting, one completed.</summary>
    private static async Task SaveRunsInEveryState(IServiceProvider provider, TenantWorld world)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        var live = CoreRows.NewRun(bot, world.UserA1, RunStatus.Running);
        live.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var lost = CoreRows.NewRun(bot, world.UserA1, RunStatus.Running);
        lost.LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        session.Db.Runs.AddRange(
            CoreRows.NewRun(bot, world.UserA1),
            CoreRows.NewRun(bot, world.UserA1),
            live,
            lost,
            CoreRows.NewRun(bot, world.UserA1, RunStatus.Waiting),
            CoreRows.NewRun(bot, world.UserA1, RunStatus.Completed));
        await session.Commit();
    }

    #endregion

    #region Nested Types

    private sealed record FlowSet(string Live, string Expired, Guid ConnectionId);

    #endregion
}
