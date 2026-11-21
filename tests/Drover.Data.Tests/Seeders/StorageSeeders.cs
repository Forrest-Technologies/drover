namespace Drover.Data.Tests.Seeders;

public sealed class SandboxSeeder : PlainSeeder<Sandbox>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["owner_bot_id"] = seed.Parent("bots"),
        ["policy"] = "private",
        ["provider"] = "test",
        ["provider_sandbox_id"] = seed.Unique("sandbox"),
        ["state"] = "creating",
    };
}

public sealed class StoredFileSeeder : PlainSeeder<StoredFile>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed)
    {
        var columns = new Dictionary<string, object?>
        {
            ["id"] = Guid.CreateVersion7(),
            ["storage_key"] = seed.Unique("key"),
            ["name"] = "notes.txt",
            ["content_type"] = "text/plain",
            ["size"] = 1L,
            ["sha256"] = new string('0', 64),
            ["created_by_user_id"] = seed.Placement.User,
            ["created_by_bot_id"] = seed.Parent("bots"),
        };

        if (seed.ParentSpaceId("projects") is { } projectSpace && projectSpace != seed.Placement.SpaceId)
        {
            columns["project_id"] = seed.Parent("projects");
        }
        else
        {
            columns["thread_id"] = seed.Parent("threads");
            columns["message_id"] = Guid.CreateVersion7();
        }

        return columns;
    }
}

public sealed class FileByteReservationSeeder : PlainSeeder<FileByteReservation>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["bytes"] = 1L,
    };
}

public sealed class PendingFileDeletionSeeder : PlainSeeder<PendingFileDeletion>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed)
    {
        var id = Guid.CreateVersion7();
        return new()
        {
            ["id"] = id,
            ["project_id"] = Guid.CreateVersion7(),
            ["storage_key"] = FileStorageKeys.For(seed.Placement.SpaceId!.Value, id),
            ["size"] = 1L,
        };
    }
}


public sealed class PendingUploadDeletionSeeder : PlainSeeder<PendingUploadDeletion>

{
    #region Public Methods

    public override async Task<IReadOnlyDictionary<string, object?>> Insert(
        IsolationHarness harness,
        TenantSession session,
        TableUnderTest table,
        RowPlacement placement,
        IReadOnlyDictionary<string, object?> values)
    {
        _ = values;
        var files = harness.Table("files");
        var source = await harness.Seed(files, placement);
        await session.Execute(
            "UPDATE files SET message_id = NULL, created_by_bot_id = NULL WHERE id = @id",
            ("id", source.Key["id"]));
        var deleted = await harness.DeleteRow(session, files, source);
        Expect.That(deleted == 1, "Deleting the source upload did not create a pending upload deletion.");
        return source.Key;
    }

    #endregion

    #region Protected Methods

    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
    };

    #endregion
}

public sealed class SecretSeeder : PlainSeeder<Secret>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["kind"] = "model_key",
        ["ciphertext"] = new byte[] { 1, 2, 3 },
        ["created_by_user_id"] = seed.Placement.User,
    };
}

public sealed class AuditLogEntrySeeder : PlainSeeder<AuditLogEntry>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["actor_kind"] = "user",
        ["actor_id"] = seed.Placement.User,
        ["action"] = "example.action",
    };
}

public sealed class MemoryNoteSeeder : PlainSeeder<MemoryNote>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["bot_id"] = seed.Parent("bots"),
        ["text"] = "Prefers short answers.",
        ["written_by_bot_id"] = seed.Parent("bots"),
        ["source_thread_id"] = Guid.CreateVersion7(),
        ["source_message_id"] = Guid.CreateVersion7(),
    };
}

public sealed class SandboxSecretSeeder : PlainSeeder<SandboxSecret>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["name"] = seed.Unique("SAMPLE_TOKEN").ToUpperInvariant().Replace('-', '_'),
        ["description"] = string.Empty,
        ["mode"] = "brokered",
        ["hosts"] = new List<string> { "api.example.com" },
        ["allow_in_query"] = false,
        ["allow_in_body"] = false,
        ["scope"] = "project",
        ["project_id"] = seed.Parent("projects"),
        ["secret_id"] = Guid.CreateVersion7(),
        ["created_by_user_id"] = seed.Placement.User,
    };
}

public sealed class SandboxSecretBotSeeder : PlainSeeder<SandboxSecretBot>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["sandbox_secret_id"] = seed.Parent("sandbox_secrets"),
        ["bot_id"] = seed.Parent("bots"),
        ["name"] = seed.Unique("SAMPLE_TOKEN").ToUpperInvariant().Replace('-', '_'),
    };
}

public sealed class AllowAllWindowSeeder : PlainSeeder<AllowAllWindow>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["bot_id"] = seed.Parent("bots"),
        ["kind"] = "hour_1",
        ["ends_at"] = DateTimeOffset.UtcNow.AddHours(1),
        ["started_at"] = DateTimeOffset.UtcNow,
        ["started_from"] = "bot",
    };
}
