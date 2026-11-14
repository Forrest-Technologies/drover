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
        ["claim_runs", "find_user_for_sign_in", "memberships_for_user", "resolve_oauth_state", "tool_sign_ins_running_out", "forget_tool_credentials", "sandboxes_to_sweep", "purge_expired", "health"];

    #endregion

    #region Sign-In

    [Fact]
    public async Task Sign_in_finds_a_user_by_email_with_no_tenant_set_and_ignores_case()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var email = $"{world.UserA2:N}@Example.Test";

        var found = await Query(postgres.AppConnectionString(), "SELECT user_id FROM sys.find_user_for_sign_in(@email)", ("email", email));
        var unknown = await Query(postgres.AppConnectionString(), "SELECT user_id FROM sys.find_user_for_sign_in(@email)", ("email", "nobody@example.test"));

        found.Should().ContainSingle().Which[0].Should().Be(world.UserA2);
        unknown.Should().BeEmpty();
    }

    [Fact]
    public async Task Sign_in_returns_the_user_id_and_nothing_else()
    {
        var columns = await Query(
            postgres.AdminConnectionString,
            """
            SELECT array_agg(a.attname::text ORDER BY a.attnum)
            FROM pg_proc p, unnest(p.proargnames, p.proargmodes) WITH ORDINALITY a(attname, mode, attnum)
            WHERE p.pronamespace = 'sys'::regnamespace AND p.proname = 'find_user_for_sign_in' AND a.mode = 't'
            """);

        ((string[])columns.Single()[0]!).Should().Equal("user_id");
    }

    [Fact]
    public async Task Memberships_are_those_of_the_transactions_own_user_and_nobody_elses()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var sql = "SELECT space_id, kind, role FROM sys.memberships_for_user()";

        var traveler = await QueryAs(world.Traveler, sql);
        var owner = await QueryAs(world.UserA1, sql);
        var other = await QueryAs(world.UserB1, sql);
        var nobody = await QueryAs(null, sql);

        traveler.Select(row => row[0]).Should().BeEquivalentTo([world.SpaceA, world.SpaceB]);
        traveler.Should().OnlyContain(row => (string)row[1]! == "shared" && (string)row[2]! == "member");
        owner.Should().ContainSingle().Which[0].Should().Be(world.SpaceA);
        other.Should().ContainSingle().Which[0].Should().Be(world.SpaceB);
        other.Select(row => row[0]).Should().NotContain(world.SpaceA, "B's context never returns A's memberships");
        owner.Select(row => row[0]).Should().NotContain(world.SpaceB);
        nobody.Should().BeEmpty("with no user set there is nobody to answer for");
    }

    [Fact]
    public async Task The_memberships_function_takes_no_argument()
    {
        var arguments = await Query(postgres.AdminConnectionString, "SELECT pronargs::int FROM pg_proc WHERE pronamespace = 'sys'::regnamespace AND proname = 'memberships_for_user'");

        arguments.Should().ContainSingle().Which[0].Should().Be(0);
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

    #region Sign-Ins Running Out

    [Fact]
    public async Task Sign_ins_running_out_are_found_in_every_space_each_in_turn_soonest_first_as_ids_and_only_the_ones_that_can_be_renewed()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var now = DateTimeOffset.UtcNow;
        var soonest = await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddMinutes(2));
        var ofTheSpace = await SaveSignIn(provider, world.SpaceA, world.UserA1, Audience.Everyone, now.AddMinutes(5));
        var inAnotherSpace = await SaveSignIn(provider, world.SpaceB, world.UserB1, world.UserB1, now.AddMinutes(8));
        await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddHours(2));
        await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, null);
        await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddMinutes(1), CredentialStatus.NeedsReconnect);
        await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddMinutes(1), connectionStatus: ConnectionStatus.Disabled);
        await SaveSignIn(provider, world.SpaceA, world.UserA1, Audience.Everyone, now.AddMinutes(1), authKind: ToolAuthKind.Header);
        const string Sql = "SELECT space_id, audience_user_id, tool_connection_id, credential_id FROM sys.tool_sign_ins_running_out(@before, @since, @renewed, @limit)";
        var since = now.AddMinutes(-30).UtcDateTime;

        var due = await Query(scratch.AppConnectionString, Sql, ("before", now.AddMinutes(15).UtcDateTime), ("since", since), ("renewed", since), ("limit", 10));
        var firstTwo = await Query(scratch.AppConnectionString, Sql, ("before", now.AddMinutes(15).UtcDateTime), ("since", since), ("renewed", since), ("limit", 2));
        var shape = await Query(
            scratch.AppConnectionString,
            "SELECT * FROM sys.tool_sign_ins_running_out(@before, @since, @renewed, @limit)",
            ("before", now.AddMinutes(15).UtcDateTime),
            ("since", since),
            ("renewed", since),
            ("limit", 1));

        due.Select(row => ((Guid)row[0]!, (Guid)row[1]!, (Guid)row[2]!, (Guid)row[3]!)).Should().Equal(
            [
                (world.SpaceA, world.UserA1, soonest.ConnectionId, soonest.CredentialId),
                (world.SpaceB, world.UserB1, inAnotherSpace.ConnectionId, inAnotherSpace.CredentialId),
                (world.SpaceA, Audience.Everyone, ofTheSpace.ConnectionId, ofTheSpace.CredentialId),
            ],
            "not the one that lasts, the one with no known end, the one already marked, the removed connection's, or a header");
        firstTwo.Should().HaveCount(2);
        shape.Single().Should().HaveCount(4, "ids only: nothing that leads to a token");
    }

    #endregion

    #region Forgetting A Connection's Sign-Ins

    [Fact]
    public async Task Forgetting_a_connections_sign_ins_takes_every_audience_in_the_callers_own_space_and_nothing_anywhere_else()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var mine = await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, null);
        var anothers = await SaveSignIn(provider, world.SpaceA, world.UserA2, world.UserA2, null, connectionId: mine.ConnectionId);
        var theSpaces = await SaveSignIn(provider, world.SpaceA, world.UserA1, Audience.Everyone, null, connectionId: mine.ConnectionId);
        var elsewhereInTheSpace = await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, null);
        var inAnotherSpace = await SaveSignIn(provider, world.SpaceB, world.UserB1, world.UserB1, null);
        const string Sql = "SELECT sys.forget_tool_credentials(@connection)";

        var withNoTenant = await Scalar<int>(scratch.AppConnectionString, Sql, ("connection", mine.ConnectionId));
        var fromAnotherSpace = await ScalarAs<int>(scratch.AppConnectionString, world.SpaceB, world.UserB1, Sql, ("connection", mine.ConnectionId));
        var forgotten = await ScalarAs<int>(scratch.AppConnectionString, world.SpaceA, world.UserA1, Sql, ("connection", mine.ConnectionId));
        var again = await ScalarAs<int>(scratch.AppConnectionString, world.SpaceA, world.UserA1, Sql, ("connection", mine.ConnectionId));

        withNoTenant.Should().Be(0, "a transaction with no space sees no space's rows");
        fromAnotherSpace.Should().Be(0, "a connection id from another space forgets nothing");
        forgotten.Should().Be(3, "every audience of that one connection: two people's and the space's");
        again.Should().Be(0);
        var left = await Query(
            AdminFor(scratch),
            "SELECT id FROM tool_connection_credentials UNION ALL SELECT id FROM secrets ORDER BY id");
        left.Select(row => (Guid)row[0]!).Should().BeEquivalentTo(
            [elsewhereInTheSpace.CredentialId, elsewhereInTheSpace.SecretId, inAnotherSpace.CredentialId, inAnotherSpace.SecretId],
            "the credential rows and the secrets of the forgotten connection are gone, and no others");
        List<Guid> forgottenSecrets = [mine.SecretId, anothers.SecretId, theSpaces.SecretId];
        forgottenSecrets.Should().NotIntersectWith(left.Select(row => (Guid)row[0]!));
    }

    #endregion

    #region Sign-Ins That Were Tried

    [Fact]
    public async Task A_sign_in_tried_and_not_renewed_lately_waits_and_those_never_tried_come_before_those_tried_long_ago()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var now = DateTimeOffset.UtcNow;
        var triedLately = await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddMinutes(1), attemptedAt: now.AddMinutes(-5));
        var triedLongAgo = await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddMinutes(2), attemptedAt: now.AddHours(-2));
        var neverTried = await SaveSignIn(provider, world.SpaceB, world.UserB1, world.UserB1, now.AddMinutes(9));
        const string Sql = "SELECT credential_id FROM sys.tool_sign_ins_running_out(@before, @since, @renewed, @limit)";

        var due = await Query(scratch.AppConnectionString, Sql, ("before", now.AddMinutes(15).UtcDateTime), ("since", now.AddMinutes(-30).UtcDateTime), ("renewed", now.UtcDateTime), ("limit", 10));
        var onlyOne = await Query(scratch.AppConnectionString, Sql, ("before", now.AddMinutes(15).UtcDateTime), ("since", now.AddMinutes(-30).UtcDateTime), ("renewed", now.UtcDateTime), ("limit", 1));

        due.Select(row => (Guid)row[0]!).Should().Equal(
            [neverTried.CredentialId, triedLongAgo.CredentialId],
            "the one tried five minutes ago waits, though it runs out soonest; the one never tried comes first");
        onlyOne.Single()[0].Should().Be(neverTried.CredentialId, "so a pass of one is not spent on a sign-in that already failed");
        triedLately.CredentialId.Should().NotBe(Guid.Empty);
    }

    #endregion

    #region Sign-Ins Of A Crowded Space

    [Fact]
    public async Task One_space_with_more_sign_ins_running_out_than_a_pass_holds_does_not_fill_the_pass()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var now = DateTimeOffset.UtcNow;
        List<Guid> crowded = [];

        // Five in one space, every one of them running out before the single sign-in of the other space.
        foreach (var minutes in (int[])[1, 2, 3, 4, 5])
        {
            crowded.Add((await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddMinutes(minutes))).CredentialId);
        }

        var theOther = await SaveSignIn(provider, world.SpaceB, world.UserB1, world.UserB1, now.AddMinutes(14));
        const string Sql = "SELECT credential_id FROM sys.tool_sign_ins_running_out(@before, @since, @renewed, @limit)";

        var pass = await Query(scratch.AppConnectionString, Sql, ("before", now.AddMinutes(15).UtcDateTime), ("since", now.AddMinutes(-5).UtcDateTime), ("renewed", now.AddMinutes(-15).UtcDateTime), ("limit", 2));
        var all = await Query(scratch.AppConnectionString, Sql, ("before", now.AddMinutes(15).UtcDateTime), ("since", now.AddMinutes(-5).UtcDateTime), ("renewed", now.AddMinutes(-15).UtcDateTime), ("limit", 10));

        pass.Select(row => (Guid)row[0]!).Should().Equal(
            [crowded[0], theOther.CredentialId],
            "one from each space before a second from any, so the other space's sign-in is renewed in the first pass after it comes due");
        all.Select(row => (Guid)row[0]!).Should().Equal([crowded[0], theOther.CredentialId, .. crowded[1..]], "after each space has had its turn, soonest first");
    }

    [Fact]
    public async Task A_sign_in_renewed_lately_is_left_out_however_soon_it_runs_out()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var now = DateTimeOffset.UtcNow;
        await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddMinutes(5), renewedAt: now.AddMinutes(-5));
        var renewedLongAgo = await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddMinutes(6), renewedAt: now.AddMinutes(-50));
        var neverRenewed = await SaveSignIn(provider, world.SpaceA, world.UserA1, world.UserA1, now.AddMinutes(7));
        const string Sql = "SELECT credential_id FROM sys.tool_sign_ins_running_out(@before, @since, @renewed, @limit)";

        var due = await Query(scratch.AppConnectionString, Sql, ("before", now.AddMinutes(15).UtcDateTime), ("since", now.AddMinutes(-5).UtcDateTime), ("renewed", now.AddMinutes(-15).UtcDateTime), ("limit", 10));

        due.Select(row => (Guid)row[0]!).Should().Equal(
            [renewedLongAgo.CredentialId, neverRenewed.CredentialId],
            "tokens that last less than the lead are due again the moment they are renewed; they wait out the lead all the same");
    }

    #endregion

    #region Sandboxes To Sweep

    [Fact]
    public async Task Sandboxes_to_sweep_are_the_due_retiring_ones_those_that_may_still_run_and_creations_left_behind_in_every_space_as_ids()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var now = DateTimeOffset.UtcNow;
        var inA = await SaveSandboxes(provider, world.SpaceA, world.UserA1, now,
        [
            (SandboxState.Retiring, now.AddMinutes(-1), null, null),
            (SandboxState.Retiring, now.AddDays(7), now.AddMinutes(10), null),
            (SandboxState.Retiring, now.AddDays(7), now.AddMinutes(-10), null),
            (SandboxState.Retiring, now.AddDays(7), null, null),
            (SandboxState.Creating, null, null, now.AddMinutes(-20)),
            (SandboxState.Creating, null, null, now.AddMinutes(-1)),
            (SandboxState.Running, now.AddMinutes(-30), now.AddMinutes(10), now.AddMinutes(-30)),
            (SandboxState.Paused, null, null, now.AddMinutes(-30)),
            (SandboxState.Destroyed, now.AddMinutes(-30), null, now.AddMinutes(-30)),
        ]);
        var inB = await SaveSandboxes(provider, world.SpaceB, world.UserB1, now, [(SandboxState.Retiring, now.AddSeconds(-1), null, null)]);
        var randomCreating = await SaveSandboxes(provider, world.SpaceB, world.UserB1, now, [(SandboxState.Creating, null, null, null)], randomIds: true);
        const string Sql = "SELECT space_id, sandbox_id FROM sys.sandboxes_to_sweep(@now, @stuck, @limit)";
        (string, object?)[] at = [("now", now.UtcDateTime), ("stuck", now.AddMinutes(-10).UtcDateTime)];

        var found = await Query(scratch.AppConnectionString, Sql, [.. at, ("limit", 10)]);
        var two = await Query(scratch.AppConnectionString, Sql, [.. at, ("limit", 2)]);
        var shape = await Query(scratch.AppConnectionString, "SELECT * FROM sys.sandboxes_to_sweep(@now, @stuck, @limit)", [.. at, ("limit", 1)]);
        var inATransaction = await QueryAs(scratch.AppConnectionString, world.UserA1, Sql, [.. at, ("limit", 10)]);

        found.Select(row => ((Guid)row[0]!, (Guid)row[1]!)).Should().BeEquivalentTo(
            [(world.SpaceA, inA[0]), (world.SpaceA, inA[1]), (world.SpaceA, inA[4]), (world.SpaceB, inB[0])],
            "the due one, the one that may still run, the creation left twenty minutes ago, and another space's due one; "
            + "not one already paused, one with no known end, a fresh creation, a live or destroyed one, or a creating row whose id holds no time");
        randomCreating.Should().ContainSingle();
        two.Should().HaveCount(2);
        shape.Single().Should().HaveCount(2, "ids only: nothing that leads to a token or a provider's sandbox");
        inATransaction.Should().HaveCount(4, "a tenant transaction gets the same answer: the function is the only way across spaces");
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

    [Fact]
    public async Task Applied_migration_names_the_newest_migration_and_the_application_role_cannot_read_the_history_itself()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        await using var scope = provider.CreateAsyncScope();
        var newest = scope.ServiceProvider.GetRequiredService<DroverDbContext>().Database.GetMigrations().Last();

        var applied = await Query(scratch.AppConnectionString, "SELECT sys.applied_migration()");
        var direct = () => Query(scratch.AppConnectionString, "SELECT count(*) FROM \"__EFMigrationsHistory\"");

        applied.Should().ContainSingle().Which.Should().Equal(newest);
        (await direct.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
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
            ("memberships_for_user", "SELECT * FROM sys.memberships_for_user()", 2, [("as_user", world.Traveler)]),
            ("resolve_oauth_state", "SELECT * FROM sys.resolve_oauth_state(@hash)", 0, [("hash", "none")]),
            ("tool_sign_ins_running_out", "SELECT * FROM sys.tool_sign_ins_running_out(@before, @since, @renewed, @limit)", 0, [("before", DateTime.UtcNow), ("since", DateTime.UtcNow), ("renewed", DateTime.UtcNow), ("limit", 10)]),
            ("forget_tool_credentials", "SELECT sys.forget_tool_credentials(@connection)", 0, [("connection", Guid.CreateVersion7())]),
            ("sandboxes_to_sweep", "SELECT * FROM sys.sandboxes_to_sweep(@now, @stuck, @limit)", 0, [("now", DateTime.UtcNow), ("stuck", DateTime.UtcNow.AddMinutes(-10)), ("limit", 10)]),
            ("purge_expired", "SELECT sys.purge_expired(@limit)", 0, [("limit", 10)]),
            ("health", "SELECT * FROM sys.health()", 1, []),
            ("applied_migration", "SELECT sys.applied_migration()", 1, []),
        };

        foreach (var call in calls)
        {
            var asUser = call.Parameters.Where(parameter => parameter.Item1 == "as_user").Select(parameter => (Guid?)parameter.Item2).FirstOrDefault();
            await QueryAs(scratch.AppConnectionString, asUser, call.Sql, [.. call.Parameters.Where(parameter => parameter.Item1 != "as_user")]);
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
                   coalesce(p.proconfig, ARRAY[]::text[]) @> ARRAY['search_path=pg_catalog, public, pg_temp'],
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
    public async Task The_system_role_holds_exactly_the_privileges_its_functions_need_and_reads_by_column()
    {
        var tables = await Query(
            postgres.AdminConnectionString,
            """
            SELECT n.nspname || '.' || c.relname || ' ' || p.privilege
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            CROSS JOIN (VALUES ('SELECT'), ('INSERT'), ('UPDATE'), ('DELETE'), ('TRUNCATE'), ('REFERENCES'), ('TRIGGER')) p(privilege)
            WHERE n.nspname IN ('public', 'sys') AND c.relkind IN ('r', 'p') AND has_table_privilege('drover_system', c.oid, p.privilege)
            """);
        var columns = await Query(
            postgres.AdminConnectionString,
            """
            SELECT c.relname || '.' || a.attname || ' ' || p.privilege
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            CROSS JOIN (VALUES ('SELECT'), ('UPDATE')) p(privilege)
            WHERE c.relnamespace = 'public'::regnamespace AND c.relkind IN ('r', 'p') AND a.attnum > 0 AND NOT a.attisdropped
              AND has_column_privilege('drover_system', c.oid, a.attnum, p.privilege)
            """);

        tables.Select(row => (string)row[0]!).Should().BeEquivalentTo(
            ["sys.audit INSERT", "public.oauth_flows DELETE", "public.sandboxes DELETE", "public.tool_connection_credentials DELETE", "public.secrets DELETE"],
            "no table-wide SELECT or UPDATE anywhere");
        columns.Select(row => (string)row[0]!).Should().BeEquivalentTo(
        [
            .. Columns("runs", "SELECT", "id space_id parent_run_id root_run_id principal_user_id status attempt lease_expires_at started_at cancel_requested_at"),
            .. Columns("runs", "UPDATE", "status status_reason attempt lease_owner lease_expires_at started_at"),
            .. Columns("tool_calls", "SELECT", "space_id run_id status sandbox_id"),
            .. Columns("users", "SELECT", "id normalized_email"),
            .. Columns("memberships", "SELECT", "space_id user_id role"),
            .. Columns("spaces", "SELECT", "id kind name slug"),
            .. Columns("oauth_flows", "SELECT", "state_hash space_id audience_user_id tool_connection_id expires_at"),
            .. Columns("sandboxes", "SELECT", "id space_id state retire_after expires_at"),
            .. Columns("tool_connection_credentials", "SELECT", "id space_id audience_user_id tool_connection_id secret_id status expires_at renew_attempted_at renewed_at"),
            .. Columns("secrets", "SELECT", "id space_id"),
            .. Columns("tool_connections", "SELECT", "id space_id auth_kind status"),
            .. Columns("__EFMigrationsHistory", "SELECT", "migration_id"),
        ]);
    }

    #endregion

    #region Private Methods

    private string AdminFor(ScratchDatabase scratch) =>
        new NpgsqlConnectionStringBuilder(postgres.AdminConnectionString)
        {
            Database = new NpgsqlConnectionStringBuilder(scratch.AppConnectionString).Database,
        }.ConnectionString;

    private static IEnumerable<string> Columns(string table, string privilege, string names) =>
        names.Split(' ').Select(column => $"{table}.{column} {privilege}");

    private Task<List<object?[]>> QueryAs(Guid? user, string sql) => QueryAs(postgres.AppConnectionString(), user, sql);

    /// <summary>Runs the query in a transaction that carries a user, as a tenant scope does, or none.</summary>
    private static async Task<List<object?[]>> QueryAs(string connectionString, Guid? user, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        if (user is { } id)
        {
            await using var set = new NpgsqlCommand("SELECT set_config('drover.user_id', @user, true)", connection, transaction);
            set.Parameters.AddWithValue("user", id.ToString());
            await set.ExecuteScalarAsync();
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var rows = new List<object?[]>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var row = new object?[reader.FieldCount];

                for (var index = 0; index < row.Length; index++)
                {
                    row[index] = await reader.IsDBNullAsync(index) ? null : reader.GetValue(index);
                }

                rows.Add(row);
            }
        }

        await transaction.CommitAsync();
        return rows;
    }

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

    /// <summary>Runs the query in a transaction carrying a space and a user, as a tenant scope does.</summary>
    private static async Task<T> ScalarAs<T>(string connectionString, Guid space, Guid user, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await using (var set = new NpgsqlCommand("SELECT set_config('drover.space_id', @space, true), set_config('drover.user_id', @user, true)", connection, transaction))
        {
            set.Parameters.AddWithValue("space", space.ToString());
            set.Parameters.AddWithValue("user", user.ToString());
            await set.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var scalar = (T)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
        await transaction.CommitAsync(TestContext.Current.CancellationToken);
        return scalar;
    }

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

    /// <summary>A connection with one credential, saved as the given person. The secret is a placeholder row: nothing here reads it.</summary>
    private static async Task<(Guid ConnectionId, Guid CredentialId, Guid SecretId)> SaveSignIn(
        IServiceProvider provider,
        Guid spaceId,
        Guid userId,
        Guid audience,
        DateTimeOffset? expiresAt,
        CredentialStatus status = CredentialStatus.Active,
        ConnectionStatus connectionStatus = ConnectionStatus.Ready,
        ToolAuthKind authKind = ToolAuthKind.Oauth,
        DateTimeOffset? attemptedAt = null,
        Guid? connectionId = null,
        DateTimeOffset? renewedAt = null)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        var connection = NewToolConnection();
        connection.Id = connectionId ?? connection.Id;
        connection.AuthKind = authKind;
        connection.Status = connectionStatus;
        connection.CredentialScope = audience == Audience.Everyone ? CredentialScope.Space : CredentialScope.PerUser;
        var secret = new Secret { AudienceUserId = audience, Kind = SecretKind.OauthTokens, Ciphertext = [0], CreatedByUserId = userId, CreatedAt = DateTimeOffset.UtcNow };
        var credential = new ToolConnectionCredential { ToolConnectionId = connection.Id, SecretId = secret.Id, AudienceUserId = audience, ExpiresAt = expiresAt, Status = status, RenewAttemptedAt = attemptedAt, RenewedAt = renewedAt };
        if (connectionId is null)
        {
            session.Db.ToolConnections.Add(connection);
        }

        session.Db.Secrets.Add(secret);
        session.Db.ToolConnectionCredentials.Add(credential);
        await session.Commit();
        return (connection.Id, credential.Id, secret.Id);
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

    /// <summary>
    /// One bot per sandbox, each saved as the given person: state, when it may be destroyed, when it pauses on its own,
    /// and when it was made, which a version 7 id carries. <paramref name="randomIds"/> gives version 4 ids instead.
    /// </summary>
    private static async Task<List<Guid>> SaveSandboxes(
        IServiceProvider provider,
        Guid spaceId,
        Guid userId,
        DateTimeOffset now,
        (SandboxState State, DateTimeOffset? RetireAfter, DateTimeOffset? ExpiresAt, DateTimeOffset? MadeAt)[] rows,
        bool randomIds = false)
    {
        await using var session = await TenantSession.Open(provider, spaceId, userId);
        var model = CoreRows.NewConnection();
        session.Db.ModelConnections.Add(model);
        List<Guid> ids = [];

        foreach (var (state, retireAfter, expiresAt, madeAt) in rows)
        {
            var bot = CoreRows.NewBot(model);
            CoreRows.AddBot(session.Db, bot, []);
            var sandbox = NewSandbox(bot, state);
            sandbox.Id = randomIds ? Guid.NewGuid() : Guid.CreateVersion7(madeAt ?? now);
            sandbox.RetireAfter = retireAfter;
            sandbox.ExpiresAt = expiresAt;
            session.Db.Sandboxes.Add(sandbox);
            ids.Add(sandbox.Id);
        }

        await session.Commit();
        return ids;
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
