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
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
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
