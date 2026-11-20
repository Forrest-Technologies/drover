namespace Drover.Data.Tests;

/// <summary>Database boundaries for discovering abandoned uploads and preserving durable deletion receipts.</summary>
[Collection(PostgresFixtureDefinition.Name)]
public sealed class UploadDeletionSecurityTests(PostgresFixture postgres)
{
    #region Public Methods

    [Fact]
    public async Task A_receipt_cannot_be_forged_retargeted_or_created_from_a_temporary_table()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var upload = (await SaveUploads(provider, world, 1)).Single();
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            (await session.Execute("DELETE FROM files WHERE id = @id", ("id", upload.Id))).Should().Be(1);
            await session.Commit();
        }

        var forged = () => Execute(
            scratch.AppConnectionString,
            "INSERT INTO pending_upload_deletions (id, space_id, audience_user_id) VALUES (@id, @space, @user)",
            ("id", Guid.CreateVersion7()),
            ("space", world.SpaceA),
            ("user", world.UserA1));
        (await forged.Should().ThrowAsync<PostgresException>()).Which.SqlState
            .Should().Be(PostgresErrorCodes.InsufficientPrivilege);

        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            var retarget = () => session.Execute(
                "UPDATE pending_upload_deletions SET id = @other WHERE id = @id",
                ("id", upload.Id),
                ("other", Guid.CreateVersion7()));
            (await retarget.Should().ThrowAsync<PostgresException>()).Which.SqlState
                .Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        }

        var temporaryTrigger = () => Execute(
            scratch.AppConnectionString,
            """
            CREATE TEMP TABLE forged_files (id uuid);
            CREATE TRIGGER forged_receipt AFTER DELETE ON forged_files
              FOR EACH ROW EXECUTE FUNCTION sys.queue_upload_deletion();
            INSERT INTO forged_files VALUES (@id);
            DELETE FROM forged_files;
            """,
            ("id", Guid.CreateVersion7()));
        var database = new NpgsqlConnectionStringBuilder(scratch.AppConnectionString).Database
            ?? throw new InvalidOperationException("The scratch database has no name.");
        await Execute(
            AdminFor(scratch),
            $"GRANT TEMPORARY ON DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(database)} TO drover_app");
        var triggerRefusal = await temporaryTrigger.Should().ThrowAsync<PostgresException>();
        triggerRefusal.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
        triggerRefusal.Which.MessageText.Should().Contain("only from deleted stored files");

        (await Scalar<long>(
            AdminFor(scratch),
            "SELECT count(*) FROM sys.audit WHERE function_name = 'queue_upload_deletion' AND row_count = 1"))
            .Should().Be(1);
    }

    [Fact]
    public async Task Discovery_uses_the_database_cutoff_returns_ids_only_and_caps_a_pass_at_fifty()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var uploads = await SaveUploads(provider, world, 52);
        var exactCutoff = uploads[^1];
        await using var connection = new NpgsqlConnection(scratch.AppConnectionString);
        await connection.OpenAsync(Token);
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await SetTenant(connection, transaction, world.SpaceA, world.UserA1);
        await using (var age = new NpgsqlCommand(
            """
            UPDATE files
            SET created_at = CASE WHEN id = @exact THEN now() - interval '1 day'
                                  ELSE now() - interval '1 day 1 second' END
            """,
            connection,
            transaction))
        {
            age.Parameters.AddWithValue("exact", exactCutoff.Id);
            (await age.ExecuteNonQueryAsync(Token)).Should().Be(52);
        }

        await using var discover = new NpgsqlCommand("SELECT * FROM sys.expired_uploads(500)", connection, transaction);
        await using var reader = await discover.ExecuteReaderAsync(Token);
        List<Guid> found = [];

        while (await reader.ReadAsync(Token))
        {
            reader.FieldCount.Should().Be(4, "discovery returns tenant and row ids only");
            found.Add(reader.GetGuid(2));
        }

        await reader.DisposeAsync();
        await transaction.CommitAsync(Token);

        found.Should().HaveCount(50);
        found.Should().NotContain(exactCutoff.Id, "an upload exactly one day old is still retained");
        (await Scalar<int>(
            AdminFor(scratch),
            "SELECT row_count FROM sys.audit WHERE function_name = 'expired_uploads' ORDER BY id DESC LIMIT 1"))
            .Should().Be(50);
    }

    [Fact]
    public async Task Receipt_claiming_is_id_only_audited_and_caps_a_pass_at_fifty()
    {
        await using var scratch = await postgres.CreateScratchDatabase();
        await using var provider = scratch.BuildServices();
        var world = await TenantWorld.Seed(provider);
        var uploads = await SaveUploads(provider, world, 51);
        await using (var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1))
        {
            (await session.Execute(
                "DELETE FROM files WHERE id = ANY(@ids)",
                ("ids", uploads.Select(upload => upload.Id).ToArray()))).Should().Be(51);
            await session.Commit();
        }

        await using var connection = new NpgsqlConnection(scratch.AppConnectionString);
        await connection.OpenAsync(Token);
        await using var claim = new NpgsqlCommand(
            "SELECT * FROM sys.claim_pending_upload_deletions(500)",
            connection);
        await using var reader = await claim.ExecuteReaderAsync(Token);
        var count = 0;

        while (await reader.ReadAsync(Token))
        {
            reader.FieldCount.Should().Be(3, "a claim returns tenant and file ids only");
            count++;
        }

        count.Should().Be(50);
        (await Scalar<int>(
            AdminFor(scratch),
            "SELECT row_count FROM sys.audit WHERE function_name = 'claim_pending_upload_deletions' ORDER BY id DESC LIMIT 1"))
            .Should().Be(50);
    }

    #endregion

    #region Private Methods

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string AdminFor(ScratchDatabase scratch) =>
        new NpgsqlConnectionStringBuilder(postgres.AdminConnectionString)
        {
            Database = new NpgsqlConnectionStringBuilder(scratch.AppConnectionString).Database,
        }.ConnectionString;

    private static async Task<List<StoredFile>> SaveUploads(
        IServiceProvider provider,
        TenantWorld world,
        int count)
    {
        await using var session = await TenantSession.Open(provider, world.SpaceA, world.UserA1);
        var connection = CoreRows.NewConnection();
        var bot = CoreRows.NewBot(connection);
        session.Db.ModelConnections.Add(connection);
        CoreRows.AddBot(session.Db, bot, []);
        var thread = CoreRows.NewThread(bot, world.UserA1, world.UserA1);
        session.Db.Threads.Add(thread);
        var files = Enumerable.Range(0, count).Select(index =>
        {
            var id = Guid.CreateVersion7();
            return new StoredFile
            {
                Id = id,
                AudienceUserId = world.UserA1,
                ThreadId = thread.Id,
                StorageKey = FileStorageKeys.For(world.SpaceA, id),
                Name = $"upload-{index}.txt",
                ContentType = "text/plain",
                Size = 1,
                Sha256 = new string('0', 64),
                CreatedByUserId = world.UserA1,
            };
        }).ToList();
        session.Db.Files.AddRange(files);
        await session.Commit();
        return files;
    }

    private static async Task SetTenant(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid spaceId,
        Guid userId)
    {
        await using var command = new NpgsqlCommand(
            "SELECT set_config('drover.space_id', @space, true), set_config('drover.user_id', @user, true)",
            connection,
            transaction);
        command.Parameters.AddWithValue("space", spaceId.ToString());
        command.Parameters.AddWithValue("user", userId.ToString());
        await command.ExecuteScalarAsync(Token);
    }

    private static async Task<int> Execute(
        string connectionString,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<T> Scalar<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Token))!;
    }

    #endregion
}
