namespace Drover.Data.Tests.Seeders;

public sealed class MembershipSeeder : TableSeeder<Membership>
{
    public override async Task<IReadOnlyDictionary<string, object?>> Row(SeedRow seed) =>
        new Dictionary<string, object?>
        {
            ["id"] = Guid.CreateVersion7(),
            ["user_id"] = await seed.CreateUser(),
            ["role"] = "member",
        };
}
