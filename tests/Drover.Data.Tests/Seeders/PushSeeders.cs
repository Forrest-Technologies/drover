namespace Drover.Data.Tests.Seeders;

public sealed class PushSubscriptionSeeder : PlainSeeder<PushSubscription>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["id"] = Guid.CreateVersion7(),
        ["endpoint"] = $"https://push.example/{seed.Unique("device")}",
        ["public_key"] = "BPublicKeyOfASeededDevice",
        ["auth_secret"] = "seeded-auth",
        ["created_at"] = DateTimeOffset.UtcNow,
    };
}

public sealed class BotPushMuteSeeder : PlainSeeder<BotPushMute>
{
    protected override Dictionary<string, object?> Columns(SeedRow seed) => new()
    {
        ["bot_id"] = seed.Parent("bots"),
        ["muted_at"] = DateTimeOffset.UtcNow,
    };
}
