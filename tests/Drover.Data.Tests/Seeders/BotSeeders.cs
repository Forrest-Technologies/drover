namespace Drover.Data.Tests.Seeders;

public sealed class BotSeeder : PlainSeeder<Bot>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["name"] = seed.Unique("bot"),
        ["handle"] = seed.Unique("handle"),
        ["description_for_bots"] = string.Empty,
        ["instructions"] = string.Empty,
        ["model_connection_id"] = seed.Parent("model_connections"),
        ["model"] = "test-model",
        ["sandbox_policy"] = "none",
        ["status"] = "active",
        ["depth"] = 0,
    };
}

public sealed class BotAncestorSeeder : PlainSeeder<BotAncestor>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["ancestor_bot_id"] = seed.Parent("bots"),
        ["descendant_bot_id"] = seed.Parent("bots"),
        ["distance"] = 0,
    };
}

public sealed class BotSandboxShareSeeder : PlainSeeder<BotSandboxShare>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["bot_id"] = seed.Parent("bots"),
        ["shared_with_bot_id"] = seed.Parent("bots"),
    };
}

public sealed class BotScheduleSeeder : PlainSeeder<BotSchedule>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["bot_id"] = seed.Parent("bots"),
        ["owner_user_id"] = seed.Placement.User,
        ["cron"] = "0 9 * * *",
        ["time_zone"] = "UTC",
        ["prompt"] = "Summarize the day.",
        ["thread_mode"] = "new_thread",
        ["enabled"] = true,
    };
}

public sealed class ToolGrantSeeder : PlainSeeder<ToolGrant>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["bot_id"] = seed.Parent("bots"),
        ["tool_id"] = seed.Parent("tools"),
        ["effect"] = "allow",
        ["review_state"] = "current",
        ["granted_by_bot_id"] = seed.Parent("bots"),
        ["granted_by_user_id"] = seed.Placement.User,
    };
}
