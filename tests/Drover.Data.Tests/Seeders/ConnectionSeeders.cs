namespace Drover.Data.Tests.Seeders;

public sealed class ModelConnectionSeeder : PlainSeeder<ModelConnection>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["name"] = seed.Unique("connection"),
        ["kind"] = "openai_compatible",
        ["auth_kind"] = "api_key",
        ["credential_scope"] = "space",
        ["status"] = "ready",
    };
}

public sealed class ModelCatalogEntrySeeder : PlainSeeder<ModelCatalogEntry>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["model_connection_id"] = seed.Parent("model_connections"),
        ["model"] = seed.Unique("model"),
        ["display_name"] = "Test model",
        ["state"] = "available",
        ["last_seen_at"] = DateTimeOffset.UtcNow,
    };
}

public sealed class ToolConnectionSeeder : PlainSeeder<ToolConnection>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["name"] = seed.Unique("server"),
        ["slug"] = seed.Unique("server"),
        ["url"] = "https://tools.example.test/mcp",
        ["transport"] = "streamable_http",
        ["auth_kind"] = "none",
        ["credential_scope"] = "space",
        ["trust"] = "untrusted",
        ["status"] = "ready",
        ["protocol_mode"] = "automatic",
    };
}

public sealed class ToolConnectionCredentialSeeder : PlainSeeder<ToolConnectionCredential>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["tool_connection_id"] = seed.Parent("tool_connections"),
        ["secret_id"] = seed.Parent("secrets"),
        ["status"] = "active",
    };
}

public sealed class OAuthFlowSeeder : PlainSeeder<OAuthFlow>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["state_hash"] = seed.Unique("state"),
        ["tool_connection_id"] = seed.Parent("tool_connections"),
        ["expires_at"] = DateTimeOffset.UtcNow.AddMinutes(10),
    };
}

public sealed class ToolSeeder : PlainSeeder<Tool>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["source_kind"] = "mcp",
        ["tool_connection_id"] = seed.Parent("tool_connections"),
        ["name"] = seed.Unique("tool"),
        ["description"] = string.Empty,
        ["definition_hash"] = seed.Unique("hash"),
        ["sensitivity"] = "read",
        ["state"] = "available",
        ["first_seen_at"] = DateTimeOffset.UtcNow,
        ["last_seen_at"] = DateTimeOffset.UtcNow,
    };
}
