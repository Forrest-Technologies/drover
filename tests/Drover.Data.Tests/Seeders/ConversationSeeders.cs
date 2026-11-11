namespace Drover.Data.Tests.Seeders;

public sealed class ChatThreadSeeder : PlainSeeder<ChatThread>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["bot_id"] = seed.Parent("bots"),
        ["owner_user_id"] = seed.Placement.User,
        ["origin"] = "chat",
    };
}

public sealed class MessageSeeder : PlainSeeder<Message>
{
    // A thread's positions are unique, and the suite may put several rows under one parent, so each row takes the next.
    private static int position;

    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["thread_id"] = seed.Parent("threads"),
        ["position"] = Interlocked.Increment(ref position),
        ["role"] = "user",
        ["author_user_id"] = seed.Placement.User,
        ["author_bot_id"] = seed.Parent("bots"),
        ["run_id"] = seed.Parent("runs"),
        ["parts"] = """[{"type":"text","text":"hello"}]""",
    };
}

public sealed class RunSeeder : PlainSeeder<Run>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed)
    {
        var id = Guid.CreateVersion7();
        return new Dictionary<string, object?>
        {
            ["id"] = id,
            ["thread_id"] = Guid.CreateVersion7(),
            ["bot_id"] = seed.Parent("bots"),
            ["principal_user_id"] = seed.Placement.User,
            ["trigger"] = "message",
            ["root_run_id"] = id,
            ["depth"] = 0,
            ["status"] = "queued",
            ["tainted"] = false,
            ["attempt"] = 0,
            ["last_event_seq"] = 0L,
        };
    }
}

public sealed class RunEventSeeder : PlainSeeder<RunEvent>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["root_run_id"] = seed.Parent("runs"),
        ["seq"] = 1L,
        ["run_id"] = seed.Parent("runs"),
        ["thread_id"] = seed.Parent("threads"),
        ["type"] = "run.queued",
    };
}

public sealed class ToolCallSeeder : PlainSeeder<ToolCall>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["run_id"] = seed.Parent("runs"),
        ["thread_id"] = seed.Parent("threads"),
        ["tool_id"] = seed.Parent("tools"),
        ["tool_name"] = "example",
        ["status"] = "running",
        ["is_error"] = false,
        ["decided_by_user_id"] = seed.Placement.User,
        ["child_run_id"] = seed.Parent("runs"),
        ["sandbox_id"] = seed.Parent("sandboxes"),
    };
}

public sealed class UsageRecordSeeder : PlainSeeder<UsageRecord>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["run_id"] = seed.Parent("runs"),
        ["root_run_id"] = seed.Parent("runs"),
        ["bot_id"] = seed.Parent("bots"),
        ["model_connection_id"] = seed.Parent("model_connections"),
        ["model"] = "test-model",
        ["input_tokens"] = 10L,
        ["output_tokens"] = 5L,
        ["cached_tokens"] = 0L,
        ["cost"] = 0.01m,
        ["currency"] = "USD",
    };
}

public sealed class AgentSessionSeeder : PlainSeeder<AgentSession>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["thread_id"] = seed.Parent("threads"),
        ["bot_id"] = seed.Parent("bots"),
        ["framework_version"] = "test",
    };
}
