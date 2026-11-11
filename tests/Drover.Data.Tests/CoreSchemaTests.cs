namespace Drover.Data.Tests;

/// <summary>
/// The parts of the core schema that the generated isolation checks do not reach: ownership classes as the data
/// model document gives them, privileges narrower than the policies, partitioning, and the JSON columns.
/// </summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class CoreSchemaTests(PostgresFixture postgres)
{
    #region Ownership

    [Fact]
    public void The_core_tables_are_classed_as_the_data_model_says()
    {
        var classes = OwnershipMap.Current.Tables.ToDictionary(table => table.Table, table => table.Class);
        string[] space =
        [
            "bots", "bot_ancestors", "bot_sandbox_shares", "bot_schedules", "tool_grants", "model_connections", "model_catalog",
            "tool_connections", "tools", "runs", "usage_records", "sandboxes", "audit_log",
        ];
        string[] withAudience =
        [
            "tool_connection_credentials", "oauth_flows", "threads", "messages", "run_events", "tool_calls", "agent_sessions",
            "files", "secrets",
        ];

        foreach (var table in space)
        {
            classes[table].Should().Be(OwnershipClass.Space, $"{table} is space-owned");
        }

        foreach (var table in withAudience)
        {
            classes[table].Should().Be(OwnershipClass.SpaceWithAudience, $"{table} is space-owned with an audience");
        }
    }

    [Fact]
    public void Tables_the_data_model_leaves_for_later_are_not_created()
    {
        var tables = OwnershipMap.Current.Tables.Select(table => table.Table).ToList();

        tables.Should().NotContain(["workflow_checkpoints", "memories", "skills", "bot_skills"]);
    }

    #endregion

    #region Privileges

    [Fact]
    public async Task The_audit_log_is_append_only_for_the_application_role()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var entry = new AuditLogEntry { ActorKind = ActorKind.User, ActorId = world.UserA1, Action = "bot.created", Data = CoreRows.EmptyJson() };
        await using (var writer = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            writer.Db.AuditLog.Add(entry);
            await writer.Commit();
        }

        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        (await session.Db.AuditLog.CountAsync(found => found.Id == entry.Id, TestContext.Current.CancellationToken)).Should().Be(1);
        var update = () => session.Execute("UPDATE audit_log SET action = 'edited' WHERE id = @id", ("id", entry.Id));
        var thrown = await update.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        await using var other = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var delete = () => other.Execute("DELETE FROM audit_log WHERE id = @id", ("id", entry.Id));
        var refused = await delete.Should().ThrowAsync<PostgresException>();
        refused.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_application_role_may_update_every_column_of_a_thread_except_its_owner()
    {
        var rows = await Query(
            """
            SELECT a.attname, has_column_privilege('drover_app', 'public.threads', a.attname, 'UPDATE')
            FROM pg_attribute a
            WHERE a.attrelid = 'public.threads'::regclass AND a.attnum > 0 AND NOT a.attisdropped
            """);

        var withoutPrivilege = rows.Where(row => !(bool)row[1]!).Select(row => (string)row[0]!);
        withoutPrivilege.Should().Equal("owner_user_id");
    }

    #endregion

    #region Partitions

    [Fact]
    public async Task Run_events_is_partitioned_by_month_with_a_default_partition_and_no_direct_access_to_a_partition()
    {
        var partitions = await Query(
            """
            SELECT c.relname, has_table_privilege('drover_app', c.oid, 'SELECT'), pg_get_expr(c.relpartbound, c.oid)
            FROM pg_inherits i
            JOIN pg_class c ON c.oid = i.inhrelid
            WHERE i.inhparent = 'public.run_events'::regclass
            ORDER BY c.relname
            """);
        var kind = await Query("SELECT relkind::text, pg_get_partkeydef(oid) FROM pg_class WHERE oid = 'public.run_events'::regclass");

        kind.Should().ContainSingle().Which.Should().Equal("p", "RANGE (created_at)");
        partitions.Select(row => (string)row[0]!).Should().Contain(["run_events_2026_11", "run_events_2027_12", "run_events_default"]);
        partitions.Should().OnlyContain(row => !(bool)row[1]!, "the application role reads through the parent, whose policies apply");
    }

    [Fact]
    public async Task An_event_lands_in_the_partition_of_its_month_or_in_the_default_one()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world);
        var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
        var run = CoreRows.NewRun(bot, world.UserA1);
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            session.Db.Threads.Add(thread);
            session.Db.Runs.Add(run);
            await session.Commit();
        }

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            await InsertEventAt(session, world, run, thread, seq: 1, "2027-03-15 12:00:00+00");
            await InsertEventAt(session, world, run, thread, seq: 2, "2035-01-01 00:00:00+00");
            await session.Commit();
        }

        var placed = await Query(
            "SELECT seq, tableoid::regclass::text FROM run_events WHERE root_run_id = @run ORDER BY seq",
            AdminConnection(),
            ("run", run.Id));
        placed.Select(row => (string)row[1]!).Should().Equal("run_events_2027_03", "run_events_default");
    }

    [Fact]
    public async Task Another_space_cannot_see_an_event_through_the_partitioned_table()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world);
        var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
        var run = CoreRows.NewRun(bot, world.UserA1);
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            session.Db.Threads.Add(thread);
            session.Db.Runs.Add(run);
            await session.Commit();
        }

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            await InsertEventAt(session, world, run, thread, seq: 1, "2027-03-15 12:00:00+00");
            await session.Commit();
        }

        await using var asB = await TenantSession.Open(provider, world.SpaceB, world.UserB1);
        (await asB.Db.RunEvents.CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    #endregion

    #region Json Columns

    [Fact]
    public async Task Settings_records_are_stored_as_snake_case_json_and_read_back_equal()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        bot.ModelSettings = new ModelSettings { Temperature = 0.2, MaxOutputTokens = 1000, ReasoningEffort = "high", WebSearch = WebSearchMode.Automatic };
        bot.MemorySettings = new MemorySettings { Enabled = true };
        bot.SandboxSettings = new SandboxSettings { Network = NetworkAccess.Allowlist, AllowedHosts = ["a.example.test"], IdleTimeoutMinutes = 15 };
        bot.BuildLimits = new BuildLimits { MaxChildren = 3, MaxDepthBelow = 2 };
        bot.Avatar = new BotAvatar { Emoji = "x" };
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            session.Db.ModelConnections.Add(connection);
            CoreRows.AddBot(session.Db, bot, []);
            await session.Commit();
        }

        await using var reader = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var stored = await reader.Db.Bots.SingleAsync(found => found.Id == bot.Id, TestContext.Current.CancellationToken);
        var raw = await reader.Db.Database
            .SqlQuery<string>($"SELECT model_settings::text AS \"Value\" FROM bots WHERE id = {bot.Id}")
            .SingleAsync(TestContext.Current.CancellationToken);

        stored.ModelSettings.Should().BeEquivalentTo(bot.ModelSettings);
        stored.MemorySettings.Should().BeEquivalentTo(bot.MemorySettings);
        stored.SandboxSettings.Should().BeEquivalentTo(bot.SandboxSettings);
        stored.BuildLimits.Should().BeEquivalentTo(bot.BuildLimits);
        stored.Avatar.Should().BeEquivalentTo(bot.Avatar);
        raw.Should().Contain("\"max_output_tokens\": 1000").And.Contain("\"web_search\": \"automatic\"").And.Contain("\"reasoning_effort\": \"high\"");
    }

    [Fact]
    public async Task Message_parts_keep_their_kind_through_the_json_column()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world);
        var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
        var file = Guid.CreateVersion7();
        var call = Guid.CreateVersion7();
        var arguments = JsonDocument.Parse("""{"query":"weather"}""").RootElement.Clone();
        var message = CoreRows.NewMessage(thread, 1, "unused");
        message.Parts =
        [
            new TextPart("hello"),
            new ReasoningPart("thinking"),
            new ImagePart(file, "image/png", "a chart"),
            new FilePart(file, "notes.txt", "text/plain"),
            new ToolCallPart(call, "search", arguments),
            new ToolResultPart(call, false, arguments),
        ];
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            session.Db.Threads.Add(thread);
            session.Db.Messages.Add(message);
            await session.Commit();
        }

        await using var reader = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var stored = await reader.Db.Messages.SingleAsync(found => found.Id == message.Id, TestContext.Current.CancellationToken);
        var kinds = await reader.Db.Database
            .SqlQuery<string>($"SELECT part->>'type' AS \"Value\" FROM messages, jsonb_array_elements(parts) part WHERE id = {message.Id}")
            .ToListAsync(TestContext.Current.CancellationToken);

        stored.Parts.Select(part => part.GetType()).Should().Equal(
            typeof(TextPart), typeof(ReasoningPart), typeof(ImagePart), typeof(FilePart), typeof(ToolCallPart), typeof(ToolResultPart));
        stored.Parts[0].Should().Be(new TextPart("hello"));
        stored.Parts[2].Should().Be(new ImagePart(file, "image/png", "a chart"));
        kinds.Should().Equal("text", "reasoning", "image", "file", "tool_call", "tool_result");
    }

    [Fact]
    public async Task The_database_refuses_a_value_the_enum_does_not_have()
    {
        await using var provider = postgres.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var bot = await SaveBot(provider, world);
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);

        var bogus = () => session.Execute("UPDATE bots SET status = 'bogus' WHERE id = @id", ("id", bot.Id));

        var thrown = await bogus.Should().ThrowAsync<PostgresException>();
        thrown.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
    }

    #endregion

    #region Column Types

    [Fact]
    public async Task Every_column_has_a_built_in_type_so_no_vendor_or_framework_type_is_in_the_database()
    {
        var foreign = await Query(
            """
            SELECT c.relname || '.' || a.attname || ' ' || format_type(a.atttypid, a.atttypmod)
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            JOIN pg_type t ON t.oid = a.atttypid
            WHERE c.relnamespace = 'public'::regnamespace AND c.relkind IN ('r', 'p') AND a.attnum > 0 AND NOT a.attisdropped
              AND t.typnamespace <> 'pg_catalog'::regnamespace
            """);

        foreign.Should().BeEmpty();
    }

    #endregion

    #region Private Methods

    private string AdminConnection() => postgres.AdminConnectionString;

    private async Task<List<object?[]>> Query(string sql) => await Query(sql, postgres.AppConnectionString());

    private static async Task<List<object?[]>> Query(string sql, string connectionString, params (string Name, object? Value)[] parameters)
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

    private static async Task<Bot> SaveBot(IServiceProvider provider, TenantWorld world)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        await session.Commit();
        return bot;
    }

    private static async Task InsertEventAt(TenantSession session, TenantWorld world, Run run, ChatThread thread, long seq, string createdAt) =>
        await session.Execute(
            """
            INSERT INTO run_events (space_id, root_run_id, seq, created_at, run_id, thread_id, audience_user_id, type)
            VALUES (@space, @run, @seq, @createdAt::timestamptz, @run, @thread, @audience, 'message.delta')
            """,
            ("space", world.SpaceA),
            ("run", run.Id),
            ("seq", seq),
            ("createdAt", createdAt),
            ("thread", thread.Id),
            ("audience", thread.AudienceUserId));

    #endregion
}
