namespace Drover.Data.Tests.Seeders;

public sealed class UserSessionSeeder : TableSeeder<UserSession>
{
    public override Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed) =>
        Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>
        {
            ["id"] = Guid.CreateVersion7(),
            ["created_at"] = DateTimeOffset.UtcNow,
            ["last_seen_at"] = DateTimeOffset.UtcNow,
        });
}
